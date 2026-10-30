Shader "CompositeBody/VolumetricPoint"
{
    // A point light with air around it, solved in closed form instead of marched.
    //
    // WHY ANALYTIC HERE WHEN THE CONE IS MARCHED
    //
    // VolumetricSpot marches because its density is SHAPED: edge gain, range fade, the pool at
    // the far end, an optional gobo. None of that has an integral. A bare point light in uniform
    // air has no shape to it, and that integral is a standard result -- Sun et al. (SIGGRAPH
    // 2005) and Pegoraro & Parker (CGF 2009) both solve the air-light term for an isotropic
    // point source in a homogeneous medium. For an optically thin room, which a five-metre space
    // with light haze is, it collapses to an arctangent:
    //
    //     I = (1/h) * [ atan((t1 - s)/h) - atan((t0 - s)/h) ]
    //
    // where s is where the ray passes closest to the light and h is how far away it passes. That
    // is the whole integral -- one rsqrt, two atans, no loop.
    //
    // THE REASON THIS MATTERS IS NOT COST, IT IS MOVEMENT. VolumetricSpot's dither is
    // deliberately NOT animated: its own comment says crawling noise reads as the world being
    // unstable in a headset, and that is safe because those lamps hang still. These lights orbit.
    // A marched version would drag its step banding across a grain that stays put, which is
    // exactly the failure that comment warns about. A closed form has no steps to band, so it
    // needs no dither at all.
    //
    // The proxy is a sphere and the scale sets the range: radius 0.5 in object space, so a proxy
    // at scale 2R reaches R metres. Cull Front, because walking into the light is the beat.

    Properties
    {
        [HDR] _Color ("Colour", Color) = (0.62, 0.22, 1.0, 1)
        _Intensity ("Intensity", Range(0, 8)) = 1.6
        _Density ("Density (per metre)", Range(0, 4)) = 1

        [Header(Shape)]
        _SourceRadius ("Source Radius (m)", Range(0.005, 0.5)) = 0.06
        _EdgeSoftness ("Edge Softness", Range(0.01, 1)) = 0.45
        _Rolloff ("Highlight Rolloff", Range(0.05, 4)) = 1.1
        _Gamma ("Falloff Gamma", Range(0.2, 3)) = 1.0

        [Header(Scattering)]
        _Anisotropy ("Forward Scattering", Range(0, 0.9)) = 0.35
        _HazeCoupling ("Follow The Room Haze", Range(0, 1)) = 1

        [Header(Reveal)]
        _Reveal ("Reveal", Range(0, 1)) = 1

        [Header(Occlusion)]
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTestMode ("ZTest", Float) = 4
        _DepthSoftness ("Intersection Softness (m)", Range(0.01, 2)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+100"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Sphere"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "VolumetricPoint"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest [_ZTestMode]
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
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
                float _SourceRadius;
                float _EdgeSoftness;
                float _Rolloff;
                float _Gamma;
                float _Anisotropy;
                float _HazeCoupling;
                float _Reveal;
                float _ZTestMode;
                float _DepthSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float4 screenPos   : TEXCOORD1;
                float3 centreWS    : TEXCOORD2;
                float  radiusWS    : TEXCOORD3;
                float  fogCoord    : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.screenPos = ComputeScreenPos(posInputs.positionCS);

                // The light is the proxy's origin, and its range is the proxy's radius. Taken from
                // the transform rather than from a property so moving or scaling the object in the
                // scene is the whole authoring interface -- there is no second number to keep in
                // step with the first.
                OUT.centreWS = TransformObjectToWorld(float3(0, 0, 0));
                OUT.radiusWS = length(TransformObjectToWorldDir(float3(0.5, 0, 0)));

                OUT.fogCoord = ComputeFogFactor(posInputs.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                if (_Reveal <= 0.002) discard;

                float3 camera = GetCameraPositionWS();
                float3 toFragment = IN.positionWS - camera;
                float span = length(toFragment);
                if (span < 1e-5) discard;
                float3 dir = toFragment / span;

                // --- the span of ray inside the light's sphere -------------------------------
                // Solved rather than taken from the proxy's faces: the back face alone would give
                // the far intersection only, and the near one is where the integral starts
                // whenever the camera is outside.
                float3 toCentre = IN.centreWS - camera;
                float s = dot(toCentre, dir);                 // ray parameter of closest approach
                float h2 = max(dot(toCentre, toCentre) - s * s, 0.0);
                float R = max(IN.radiusWS, 1e-4);

                float inside = R * R - h2;
                if (inside <= 0.0) discard;                   // the ray misses the sphere entirely
                float half_ = sqrt(inside);

                float t0 = max(s - half_, 0.0);               // never behind the eye
                float t1 = s + half_;

                // --- scene occlusion ---------------------------------------------------------
                float occlusion = 1.0;
                #if defined(_SCENE_DEPTH_CLAMP)
                    float2 uv = IN.screenPos.xy / max(IN.screenPos.w, 1e-6);
                    float sceneEye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                    float fragmentEye = LinearEyeDepth(IN.positionHCS.z, _ZBufferParams);

                    // Eye depth and distance along the ray differ by the view-angle cosine; this
                    // is the same conversion the cone uses, and it is what keeps the clamp honest
                    // toward the edges of a wide field of view.
                    float tScene = sceneEye * (span / max(fragmentEye, 1e-5));

                    t1 = min(t1, tScene);
                    occlusion = saturate((tScene - t0) / max(_DepthSoftness, 1e-4));
                #endif

                if (t1 <= t0) discard;

                // --- the air-light integral, in closed form ----------------------------------
                // h is clamped to a source radius rather than to an epsilon. A ray aimed straight
                // at the light has h = 0 and the integral diverges; a real source has size, and
                // giving it one is both the physical answer and the numerical one.
                float h = max(sqrt(h2), _SourceRadius);
                float airlight = (atan((t1 - s) / h) - atan((t0 - s) / h)) / h;

                // Softened at the sphere's edge so the volume does not end on a circle. The 1/h
                // term already dims the rim; this finishes it.
                float rim = saturate((1.0 - sqrt(h2) / R) / max(_EdgeSoftness, 0.01));
                airlight *= rim * rim;

                // --- the room's haze ---------------------------------------------------------
                // Brighter where CompositeFogZone has put more air to scatter in, evaluated at
                // the closest approach: with no shape to integrate over, one sample at the point
                // that dominates the integral is the whole of the approximation.
                if (_CompositeFogColor.a > 0.0)
                {
                    float y = camera.y + dir.y * clamp(s, t0, t1);
                    float rate = _CompositeFogParams.y * _HazeCoupling;
                    airlight *= exp(-clamp((y - _CompositeFogParams.z) * rate, -8.0, 16.0));
                }

                // --- phase -------------------------------------------------------------------
                // Schlick's approximation, evaluated once at closest approach. Looking back
                // through the light flares; looking across it does not.
                float3 toLight = normalize(IN.centreWS - (camera + dir * clamp(s, t0, t1)));
                float cosT = dot(dir, -toLight);
                float g = _Anisotropy;
                float denom = 1.0 + g * cosT;
                float phase = (1.0 - g * g) / max(denom * denom, 1e-4);

                float strength = airlight * _Density * phase * _Reveal * occlusion;
                strength = pow(max(strength, 0.0), max(_Gamma, 0.2));

                // Rolled off, not clamped. URP-Performant runs m_SupportsHDR 0, so anything over
                // 1 is simply cut and the core of the light would arrive as a flat white disc.
                strength = strength / (1.0 + strength * _Rolloff);

                float3 rgb = _Color.rgb * _Intensity * strength;
                rgb = CompositeFogAttenuate(rgb, IN.positionWS, IN.fogCoord);

                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
