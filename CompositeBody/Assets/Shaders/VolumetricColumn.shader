Shader "CompositeBody/VolumetricColumn"
{
    // A column of light for O-0's 光圈 -- the purple and yellow rings the script wraps around
    // 真人 A and B in the grey void.
    //
    // There is no loop in here. The usual ways to do this are a fullscreen raymarch or a
    // stepped march inside a proxy volume, and both were ruled out by what this project
    // actually runs: URP-Performant has m_RequireDepthTexture 0, so there is no scene depth to
    // march against without paying for a prepass, and m_MSAA 4 means a fullscreen effect
    // resolves badly anyway. What is left is the cheap exact answer -- pick a density falloff
    // whose integral along a ray has a closed form, and evaluate it once per pixel.
    //
    // The falloff is (1 - r^2/R^2)^2. Squared radius keeps it a polynomial in the ray parameter,
    // so the integral is a quintic: about ten multiply-adds, no branching, no texture fetch, and
    // no banding to dither away. A Gaussian would look marginally softer and cost an erf.
    //
    // The volume is solved analytically rather than from the proxy mesh, so the result is
    // perfectly smooth however coarse the cylinder is -- a twelve-sided proxy gives the same
    // image as a hundred-sided one. The mesh only has to cover the right pixels.
    //
    // Object space is fixed: radius 0.5, y from -0.5 to +0.5 -- a unit cube's worth. The
    // proxy is a CUBE, not a cylinder, and that is deliberate. The volume is solved from the
    // ray, so the proxy's only job is to generate a fragment wherever the light might be
    // visible; a cylinder looked closer to the shape but its end caps cull from the inside, so
    // standing in the column punched wedge-shaped holes through it where no back face existed
    // to shade. A cube is closed from every interior angle, has twelve triangles instead of
    // eighty, and -- because the maths never reads the mesh -- gives an identical image.

    Properties
    {
        [HDR] _Color ("Colour", Color) = (0.62, 0.22, 1.0, 1)
        _Intensity ("Intensity", Range(0, 8)) = 1.6
        _Density ("Density", Range(0, 4)) = 1

        [Header(Shape)]
        _EdgeSoftness ("Edge Softness", Range(0.01, 1)) = 0.55
        _TopFade ("Top Fade", Range(0, 1)) = 0.45
        _BottomFade ("Bottom Fade", Range(0, 1)) = 0.12
        _CoreBoost ("Core Boost", Range(0, 4)) = 0.9
        _Rolloff ("Highlight Rolloff", Range(0.05, 4)) = 1.1
        _GroundPool ("Ground Pool", Range(0, 3)) = 1.1

        [Header(Life)]
        _Phase ("Phase (seconds, driven)", Float) = 0
        _Drift ("Drift", Range(0, 1)) = 0.25
        _DriftSpeed ("Drift Speed", Range(0, 4)) = 0.35

        [Header(Reveal)]
        _Reveal ("Reveal", Range(0, 1)) = 1

        [Header(Occlusion)]
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTestMode ("ZTest", Float) = 4
        _DepthSoftness ("Intersection Softness (m)", Range(0.01, 2)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+100"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Plane"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "VolumetricColumn"
            Tags { "LightMode" = "UniversalForward" }

            // Back faces, so the shader runs once per pixel of the volume and still draws when
            // the camera is inside it -- front faces would be clipped away the moment a player
            // steps into the light, which in a piece that asks them to walk into it is the one
            // case that has to work.
            //
            // ZTest LEqual with no depth texture is what gives the ring its ring: the figure
            // standing in the column rejects the fragments behind it, so the light reads as
            // surrounding them rather than as a fog painted over them. Hard-edged against the
            // silhouette, which is free and, for a membrane figure in a void, correct.
            Blend SrcAlpha One
            ZWrite Off
            // Driven from the material so the two occlusion strategies can share one shader.
            // LEqual lets the depth buffer do the occluding for free; Always hands that job to
            // the scene-depth clamp below, which is the only way to accumulate light in the air
            // BETWEEN the eye and nearer geometry.
            ZTest [_ZTestMode]
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            // Off by default, and off is the cheap path. On, the shader reads scene depth so
            // the column integrates correctly against the floor and the figures -- which costs
            // a depth prepass in the pipeline asset, not in here.
            #pragma multi_compile _ _SCENE_DEPTH_CLAMP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CompositeFog.hlsl"
            #if defined(_SCENE_DEPTH_CLAMP)
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #endif

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _Density;
                float _EdgeSoftness;
                float _TopFade;
                float _BottomFade;
                float _CoreBoost;
                float _Rolloff;
                float _GroundPool;
                float _ZTestMode;
                float _DepthSoftness;
                float _Phase;
                float _Drift;
                float _DriftSpeed;
                float _Reveal;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionOS  : TEXCOORD0;
                float3 cameraOS    : TEXCOORD1;
                float  fogCoord    : TEXCOORD2;
                float4 screenPos   : TEXCOORD3;
                float3 positionWS  : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = posInputs.positionCS;
                OUT.positionOS = IN.positionOS.xyz;

                // Carried per vertex and interpolated rather than recomputed per pixel. It is
                // constant across the draw, but getting it in the fragment stage means an
                // inverse-matrix multiply per pixel for a value that never changes.
                OUT.cameraOS = TransformWorldToObject(GetCameraPositionWS());

                OUT.screenPos = ComputeScreenPos(posInputs.positionCS);
                OUT.positionWS = posInputs.positionWS;
                OUT.fogCoord = ComputeFogFactor(posInputs.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                if (_Reveal <= 0.002) discard;

                // --- the ray, in the volume's own space --------------------------------------
                float3 origin = IN.cameraOS;
                float3 toFragment = IN.positionOS - origin;
                float span = length(toFragment);
                if (span < 1e-5) discard;
                float3 dir = toFragment / span;

                // --- radial support -----------------------------------------------------
                // u(t) = 1 - r(t)^2 / R^2, a downward parabola in t. The density is u^2 where
                // u > 0, so the roots of u are exactly where the ray enters and leaves the
                // light -- no mesh intersection needed, and no faceting from the proxy.
                // The volume is INSET inside the proxy: radius 0.45 and y +/-0.45 inside a
                // unit cube. The margin matters. With the two coincident, a proxy face lands
                // exactly on the light's boundary and on the floor plane, and the depth test
                // throws away the back face along those seams -- which showed up as hard-edged
                // wedges of background punched through the column. Ten percent of slack costs
                // nothing and guarantees there is always a fragment to shade.
                const float R2 = 0.2025;                 // radius 0.45, squared
                const float HALF = 0.45;
                float A = dot(dir.xz, dir.xz);
                float B = 2.0 * dot(origin.xz, dir.xz);
                float C = dot(origin.xz, origin.xz);

                float a = -A / R2;
                float b = -B / R2;
                float c = 1.0 - C / R2;

                float t0, t1;
                if (A < 1e-7)
                {
                    // Ray parallel to the axis: the radius never changes, so it is either
                    // inside for its whole length or outside for all of it.
                    if (c <= 0.0) discard;
                    t0 = 0.0;
                    t1 = span;
                }
                else
                {
                    float disc = b * b - 4.0 * a * c;
                    if (disc <= 0.0) discard;
                    float sq = sqrt(disc);
                    // a is negative, so the smaller root is (-b + sq) / (2a).
                    float inv = 0.5 / a;
                    float r0 = (-b + sq) * inv;
                    float r1 = (-b - sq) * inv;
                    t0 = min(r0, r1);
                    t1 = max(r0, r1);
                }

                // --- vertical extent ----------------------------------------------------
                // The cylinder's caps, as another interval on t, intersected with the radial one.
                if (abs(dir.y) > 1e-6)
                {
                    float ta = (-HALF - origin.y) / dir.y;
                    float tb = ( HALF - origin.y) / dir.y;
                    t0 = max(t0, min(ta, tb));
                    t1 = min(t1, max(ta, tb));
                }
                else if (abs(origin.y) > HALF)
                {
                    discard;                              // level with the ray, outside the caps
                }

                // Clamped to the segment the camera can actually see: never behind the eye,
                // and never past the back face, which ZTest has already trimmed against the
                // figure. span is the proxy's far surface, which for a cube sits outside the
                // analytic cylinder -- so this only ever trims, never extends.
                t0 = max(t0, 0.0);
                t1 = min(t1, span);

                float occlusion = 1.0;
                #if defined(_SCENE_DEPTH_CLAMP)
                    // Trim the segment at whatever the scene put in front of the far face.
                    //
                    // The conversion is simpler than it looks. This fragment sits at eye depth
                    // fragmentEye and at object-space distance span along the ray, and both
                    // scale linearly with distance from the camera -- so span/fragmentEye is
                    // the object-units-per-eye-depth for this pixel, whatever the view angle
                    // or the transform's scale. Multiply the scene's eye depth by it and the
                    // result is where the scene sits on the same t axis the integral uses.
                    float2 uv = IN.screenPos.xy / max(IN.screenPos.w, 1e-6);
                    float sceneEye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                    float fragmentEye = LinearEyeDepth(IN.positionHCS.z, _ZBufferParams);
                    float objectPerEye = span / max(fragmentEye, 1e-5);

                    float tScene = sceneEye * objectPerEye;
                    t1 = min(t1, tScene);

                    // Soften the seam so the column does not end on a hard line where it meets
                    // the floor or a figure. The softness is authored in metres, so it is
                    // converted the same way.
                    float worldSpan = max(length(IN.positionWS - GetCameraPositionWS()), 1e-5);
                    float objectPerMetre = span / worldSpan;
                    occlusion = saturate((tScene - t0) / max(_DepthSoftness * objectPerMetre, 1e-4));
                #endif

                if (t1 <= t0) discard;

                // --- the integral -------------------------------------------------------
                // F(t) = integral of (a t^2 + b t + c)^2 dt
                //      = a^2 t^5/5 + a b t^4/2 + (b^2 + 2 a c) t^3/3 + b c t^2 + c^2 t
                float a2 = a * a;
                float ab = a * b;
                float bc = b * c;
                float k3 = (b * b + 2.0 * a * c) / 3.0;
                float c2 = c * c;

                float t0_2 = t0 * t0, t1_2 = t1 * t1;
                float F0 = ((a2 * t0_2 * t0_2 * t0) / 5.0) + (ab * t0_2 * t0_2 * 0.5)
                         + (k3 * t0_2 * t0) + (bc * t0_2) + (c2 * t0);
                float F1 = ((a2 * t1_2 * t1_2 * t1) / 5.0) + (ab * t1_2 * t1_2 * 0.5)
                         + (k3 * t1_2 * t1) + (bc * t1_2) + (c2 * t1);

                float integral = max(F1 - F0, 0.0);

                // --- shaping ------------------------------------------------------------
                // Everything below is evaluated once, at the midpoint of the traversed segment,
                // rather than folded into the integral. Doing it properly would mean a separate
                // closed form per modulation and a much longer polynomial; at the thickness of
                // a light column the difference is not visible, and this keeps the whole effect
                // at one evaluation per pixel.
                float tm = (t0 + t1) * 0.5;
                float3 mid = origin + dir * tm;

                float height = saturate(mid.y / (HALF * 2.0) + 0.5); // 0 at the floor, 1 at the top
                float top = 1.0 - smoothstep(1.0 - max(_TopFade, 0.001), 1.0, height);
                float bottom = smoothstep(0.0, max(_BottomFade, 0.001), height);
                float vertical = top * bottom;

                // A brighter pool where the column meets the floor, which is what makes a shaft
                // read as standing on the ground instead of hanging in the air.
                vertical += _GroundPool * (1.0 - smoothstep(0.0, 0.22, height)) * bottom;

                // Slow asymmetry, so the light is not a perfectly static cone. Two sines on the
                // midpoint, driven by the shared phase so both headsets agree.
                float t = _Phase * _DriftSpeed;
                float drift = 1.0 + _Drift * (sin(mid.y * 2.7 + t) * 0.5 +
                                              sin(mid.x * 3.1 - t * 1.3) * 0.3 +
                                              sin(mid.z * 2.3 + t * 0.7) * 0.2);

                // The core reads brighter than the integral alone gives, because a real shaft
                // is densest where you look straight down it.
                float axial = saturate(1.0 - dot(mid.xz, mid.xz) / R2);
                float core = 1.0 + _CoreBoost * axial * axial;

                float strength = integral * _Density * vertical * drift * core * _Reveal * occlusion;

                // Edge softness as a gamma on the accumulated strength rather than on the
                // radius: it shapes how quickly the column fades out overall, and leaves the
                // analytic falloff untouched.
                strength = pow(max(strength, 0.0), max(_EdgeSoftness, 0.01) * 2.0);

                // Rolled off rather than clamped. URP-Performant runs with m_SupportsHDR 0, so
                // anything over 1 is simply cut and a long path through the column -- exactly
                // what the player sees on walking into it -- arrives as a flat sheet of colour
                // with no form left in it. x/(1+x) keeps climbing forever without ever reaching
                // 1, which costs one divide and is the whole difference between standing in
                // light and standing in front of a wall.
                strength = strength / (1.0 + strength * _Rolloff);

                float3 rgb = _Color.rgb * _Intensity * strength;
                rgb = CompositeFogMix(rgb, IN.positionWS, IN.fogCoord);
                return half4(rgb, saturate(strength) * _Color.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
