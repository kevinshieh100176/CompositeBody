Shader "CompositeBody/VolumetricSpot"
{
    // A stage light: one cone of visible haze, solved per light rather than per screen.
    //
    // WHY A CONE PER LIGHT AND NOT A FULLSCREEN FOG
    //
    // The two reference implementations for this in URP take opposite routes. Kronnect's gives
    // every light its own volume mesh -- a cone for spots -- and marches only inside it.
    // CristianQiu's is a fullscreen render feature driven by a URP Volume override, which reads
    // the real shadow maps and cookies and so gets beam shadows for free. The second is the
    // better renderer and the wrong one here, for three reasons specific to this project:
    //
    //   1. It needs post-processing enabled on the camera and the renderer, and its README lists
    //      "transparent objects do not blend correctly with the fog" as a known limitation.
    //      Nearly everything in 合成肉身 is transparent -- the membrane films, the additive point
    //      clouds, the ghost halves. That is not a corner case here, it is the whole palette.
    //   2. URP-Performant runs m_MSAA 4. A fullscreen effect forces a resolve.
    //   3. A fullscreen march is bounded by the depth buffer. A cone march is bounded by the
    //      light. That difference is the whole lesson of the Aero attempt: 4 steps across 20 m
    //      of range is one sample every five metres, and in a five-metre room the dither meant
    //      to hide that becomes the image. Twelve steps across a three-metre shaft is one sample
    //      every 25 cm -- twenty times the resolution for three times the samples.
    //
    // So: analytic ray/cone intersection to find the span the light actually occupies, then a
    // short march inside it. The cone comes from the ray, not from the proxy mesh, so the proxy
    // is a cube and the silhouette is exact however coarse the box is.
    //
    // The step count is FIXED, not adaptive. Kronnect grows the step length with path length and
    // caps the count, which is better on average. Constant cost per covered pixel is better at
    // 90 Hz in a headset, where the frame that matters is the worst one.
    //
    // OBJECT SPACE is fixed, and the scale encodes the angle. The cone is inscribed in the unit
    // cube: apex at (0, 0, -0.45), axis +Z, base of radius 0.45 at z = +0.45. So a proxy scaled
    // (2R, 2R, L) is a cone of range L and half-angle atan(R/L), and the shader needs no angle
    // parameter at all. The 0.45 rather than 0.5 is 10% of slack so no proxy face ever lands
    // exactly on the volume boundary -- coincident, the depth test throws away the back face
    // along the seam and punches hard-edged wedges of background through the light.
    //
    // Beam direction is +Z, matching UnityEngine.Light, so VolumetricSpot can read a real spot
    // light's cone and sit on top of it.

    Properties
    {
        [HDR] _Color ("Colour", Color) = (0.62, 0.22, 1.0, 1)
        _Intensity ("Intensity", Range(0, 8)) = 1.8
        _Density ("Density (per metre)", Range(0, 4)) = 1

        [Header(Shape)]
        _EdgeGain ("Edge Hardness", Range(1, 12)) = 2.2
        _EdgeSoftness ("Falloff Gamma", Range(0.05, 1)) = 0.6
        _DistanceFalloff ("Distance Falloff", Range(0, 8)) = 2.2
        _RangeFade ("Range Fade", Range(0.01, 1)) = 0.35
        _NearClip ("Near Clip (fraction of range)", Range(0, 0.5)) = 0.02
        _BaseGlow ("Pool At The Far End", Range(0, 3)) = 0.7
        _Rolloff ("Highlight Rolloff", Range(0.05, 4)) = 1.1

        [Header(Scattering)]
        _Anisotropy ("Forward Scattering", Range(0, 0.9)) = 0.65
        _Steps ("Raymarch Steps", Range(4, 32)) = 12
        _HazeCoupling ("Follow The Room Haze", Range(0, 1)) = 1

        [Header(Gobo)]
        [Toggle(_GOBO)] _GoboOn ("Use Gobo", Float) = 0
        _GoboTex ("Gobo (R)", 2D) = "white" {}
        _GoboScale ("Gobo Scale", Range(0.1, 4)) = 1
        _GoboSpin ("Gobo Spin (rad/s)", Range(-2, 2)) = 0

        [Header(Life)]
        _Phase ("Phase (seconds, driven)", Float) = 0
        _Drift ("Drift", Range(0, 1)) = 0.18
        _DriftSpeed ("Drift Speed", Range(0, 4)) = 0.35

        [Header(Reveal)]
        _Reveal ("Reveal", Range(0, 1)) = 1

        [Header(Occlusion)]
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTestMode ("ZTest", Float) = 4
        _DepthSoftness ("Intersection Softness (m)", Range(0.01, 2)) = 0.3
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
            Name "VolumetricSpot"
            Tags { "LightMode" = "UniversalForward" }

            // Pure additive. Emissive haze can only ever add light, and keeping the blend at
            // One One means _Intensity behaves linearly -- the older VolumetricColumn blends
            // SrcAlpha One against an alpha that is itself the strength, so its intensity dial
            // comes out quadratic and has to be tuned by feel.
            Blend One One
            ZWrite Off
            // Paired with the _SCENE_DEPTH_CLAMP keyword from C#; either alone is wrong. LEqual
            // lets the depth buffer occlude the beam for free. Always hands that job to the
            // depth clamp below, which is the only way to light the air BETWEEN the eye and
            // nearer geometry -- the case that exists the moment a player steps into the beam.
            ZTest [_ZTestMode]
            // Back faces: front faces clip away as soon as the camera is inside the proxy, and
            // walking into the light is the beat, not the edge case.
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma shader_feature_local _GOBO
            #pragma multi_compile _ _SCENE_DEPTH_CLAMP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CompositeFog.hlsl"
            #if defined(_SCENE_DEPTH_CLAMP)
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #endif

            TEXTURE2D(_GoboTex);
            SAMPLER(sampler_GoboTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _GoboTex_ST;
                float _Intensity;
                float _Density;
                float _EdgeGain;
                float _EdgeSoftness;
                float _DistanceFalloff;
                float _RangeFade;
                float _NearClip;
                float _BaseGlow;
                float _Rolloff;
                float _Anisotropy;
                float _Steps;
                float _HazeCoupling;
                float _GoboOn;
                float _GoboScale;
                float _GoboSpin;
                float _Phase;
                float _Drift;
                float _DriftSpeed;
                float _Reveal;
                float _ZTestMode;
                float _DepthSoftness;
            CBUFFER_END

            // The cone, inscribed in the unit cube. See the header.
            #define K_APEX   (-0.45)
            #define K_LEN    (0.9)
            #define K_SLOPE  (0.5)

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
                float3 axisWS      : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Interleaved gradient noise, written out rather than taken from the SRP core so the
            // shader does not depend on which version of Common.hlsl is in the package cache.
            //
            // Deliberately NOT animated. There is no temporal accumulation here, so animating
            // the offset would turn static grain into a crawl -- and in a headset crawling noise
            // reads as the world being unstable. Static per-pixel noise also means both eyes get
            // the same pattern at the same pixel, which keeps the dither binocularly correlated;
            // noise that differs between the eyes is noise the viewer cannot fuse.
            float SpotDither(float2 pixel)
            {
                return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = posInputs.positionCS;
                OUT.positionOS = IN.positionOS.xyz;

                // Constant across the draw, but computing it per pixel would cost an inverse
                // matrix multiply for a value that never changes.
                OUT.cameraOS = TransformWorldToObject(GetCameraPositionWS());

                // The beam axis in world space, for the phase function. Angles have to be
                // measured in world space because a cone proxy's scale is deliberately
                // non-uniform -- in object space the cone's own geometry is exact, but every
                // angle is sheared.
                OUT.axisWS = TransformObjectToWorldDir(float3(0, 0, 1));

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

                // --- the axial slab: sLo <= s <= K_LEN, s measured from the apex --------------
                // Clipped at the near end by _NearClip, which is what keeps a stage light from
                // being brightest inside its own lens housing.
                float sLo = _NearClip * K_LEN;
                float oz = origin.z - K_APEX;             // the ray's s at t = 0
                float t0, t1;
                if (abs(dir.z) > 1e-6)
                {
                    float ta = (sLo   - oz) / dir.z;
                    float tb = (K_LEN - oz) / dir.z;
                    t0 = min(ta, tb);
                    t1 = max(ta, tb);
                }
                else
                {
                    if (oz < sLo || oz > K_LEN) discard;  // level with the slab, outside it
                    t0 = 0.0;
                    t1 = span;
                }

                t0 = max(t0, 0.0);
                t1 = min(t1, span);
                if (t1 <= t0) discard;

                // --- the cone ----------------------------------------------------------------
                // f(t) = x^2 + y^2 - m^2 s^2 is negative inside the double cone. The axial slab
                // above has already thrown away the mirror sheet, and a finite cone clipped by
                // its own cap is convex -- which is what lets the A < 0 case below pick one
                // branch and stop, because a convex body meets a ray in a single interval.
                const float m2 = K_SLOPE * K_SLOPE;
                float A = dot(dir.xy, dir.xy) - m2 * dir.z * dir.z;
                float B = 2.0 * (dot(origin.xy, dir.xy) - m2 * oz * dir.z);
                float C = dot(origin.xy, origin.xy) - m2 * oz * oz;

                if (abs(A) > 1e-7)
                {
                    float disc = B * B - 4.0 * A * C;
                    if (disc <= 0.0)
                    {
                        // No crossing. An upward parabola with no roots never dips below zero,
                        // so the ray misses the cone; a downward one never rises above it, so
                        // the ray is inside along its whole length and the slab is the answer.
                        if (A > 0.0) discard;
                    }
                    else
                    {
                        float sq = sqrt(disc);
                        float inv = 0.5 / A;
                        float ra = (-B - sq) * inv;
                        float rb = (-B + sq) * inv;
                        float r0 = min(ra, rb);
                        float r1 = max(ra, rb);

                        if (A > 0.0)
                        {
                            t0 = max(t0, r0);             // inside between the roots
                            t1 = min(t1, r1);
                        }
                        else
                        {
                            // Inside OUTSIDE the roots. At most one of the two branches survives
                            // the slab, so take whichever is non-empty.
                            float hiNear = min(t1, r0);
                            if (hiNear > t0) t1 = hiNear;
                            else             t0 = max(t0, r1);
                        }
                    }
                }
                else if (abs(B) > 1e-7)
                {
                    // Ray parallel to the cone surface: f is linear, inside where B t + C <= 0.
                    float tr = -C / B;
                    if (B > 0.0) t1 = min(t1, tr);
                    else         t0 = max(t0, tr);
                }
                else if (C > 0.0)
                {
                    discard;
                }

                if (t1 <= t0) discard;

                // --- scene occlusion ---------------------------------------------------------
                // Object units per metre along THIS ray. Both the march and the authored
                // softness need it, and because it is measured along the ray it stays correct
                // under the non-uniform scale a cone proxy always has.
                float worldSpan = max(length(IN.positionWS - GetCameraPositionWS()), 1e-5);
                float objectPerMetre = span / worldSpan;

                float occlusion = 1.0;
                #if defined(_SCENE_DEPTH_CLAMP)
                    // This fragment sits at eye depth fragmentEye and at object distance span
                    // along the ray, and both scale linearly with distance from the camera -- so
                    // span/fragmentEye converts the scene's eye depth onto the same t axis the
                    // march uses, whatever the view angle or the transform's scale.
                    float2 uv = IN.screenPos.xy / max(IN.screenPos.w, 1e-6);
                    float sceneEye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                    float fragmentEye = LinearEyeDepth(IN.positionHCS.z, _ZBufferParams);
                    float tScene = sceneEye * (span / max(fragmentEye, 1e-5));

                    t1 = min(t1, tScene);
                    if (t1 <= t0) discard;

                    // Soften the seam so the beam does not end on a hard line where it crosses
                    // a figure or the floor. Authored in metres, converted the same way.
                    occlusion = saturate((tScene - t0) /
                                         max(_DepthSoftness * objectPerMetre, 1e-4));
                #endif

                // --- the march ---------------------------------------------------------------
                int steps = (int)clamp(_Steps, 4.0, 32.0);
                float dt = (t1 - t0) / steps;
                // Jitter the start by up to one step, so the banding a twelve-step march would
                // otherwise show becomes fine grain instead of visible shells.
                float t = t0 + dt * SpotDither(IN.positionHCS.xy);

                #if defined(_GOBO)
                    float spin = _GoboSpin * _Phase;
                    float goboCos = cos(spin), goboSin = sin(spin);
                    float goboK = 0.5 / max(_GoboScale, 0.01);
                #endif

                // --- the room's haze, along the beam -----------------------------------------
                // A beam is only visible because there is something in the air to scatter it, so
                // where CompositeFogZone puts more haze the beam should be BRIGHTER -- not only
                // dimmer behind it, which is all the fog attenuation at the end of this shader
                // does. Without this a shaft crossing a ground layer looks painted on top of it
                // instead of landing in it, which is the single most recognisable thing a hazer
                // does to a stage.
                //
                // The density is exp(-(y - y0)/H) and y runs linearly along the ray, so the
                // factor is a GEOMETRIC progression: compute it once at the first sample, and
                // one multiply per step after that. No exp in the loop.
                //
                // The object-space ray and the world-space one are the same line under an affine
                // transform, so t/span is the same fraction in both -- which is why this needs
                // no matrix. The authored density is therefore the density at floor level, where
                // the factor is 1, and _HazeCoupling folds into the exponent for free: at 0 the
                // rate is 0, the progression is all ones, and the beam ignores the room.
                float haze = 1.0, hazeStep = 1.0;
                if (_CompositeFogColor.a > 0.0)
                {
                    float rate = _CompositeFogParams.y * _HazeCoupling;
                    float camY = _WorldSpaceCameraPos.y;
                    float dyTotal = IN.positionWS.y - camY;
                    haze = exp(-clamp((camY + dyTotal * (t0 / span) - _CompositeFogParams.z)
                                      * rate, -8.0, 16.0));
                    hazeStep = exp(-clamp(dyTotal * (dt / span) * rate, -8.0, 8.0));
                }

                float invLen = 1.0 / K_LEN;
                float acc = 0.0;

                [loop]
                for (int i = 0; i < steps; i++)
                {
                    float3 p = origin + dir * t;
                    float s = p.z - K_APEX;               // axial distance from the apex
                    float sn = saturate(s * invLen);      // 0 at the lamp, 1 at the far end
                    float lim = K_SLOPE * s;              // the cone's radius at this depth

                    // 0 on the axis, 1 on the cone surface. The apex sits on the z axis, so the
                    // perpendicular offset is just p.xy.
                    float v2 = dot(p.xy, p.xy) / max(lim * lim, 1e-8);

                    // (1 - v^2)^2, the same density law as the column, with a gain that pushes
                    // the falloff outward and clips it -- gain 1 is a soft wash, gain 8 a crisp
                    // theatrical edge. Squaring beats a pow() twelve times over.
                    float edge = saturate((1.0 - v2) * _EdgeGain);
                    edge *= edge;

                    // Not a true inverse square. 1/s^2 is singular at the apex, and a stage
                    // light's visible haze does not obey it anyway because the beam is
                    // collimated -- the brightness falls off mostly because the cone widens,
                    // which the geometry already handles. This is bounded, has no singularity,
                    // and dials from flat to steep with one parameter.
                    float atten = rcp(1.0 + _DistanceFalloff * sn * sn);

                    float fade = 1.0 - smoothstep(1.0 - max(_RangeFade, 0.001), 1.0, sn);
                    float pool = 1.0 + _BaseGlow * smoothstep(0.55, 1.0, sn);

                    float dens = edge * atten * fade * pool;

                    #if defined(_GOBO)
                        // Normalised across the cone's cross-section, so the pattern scales with
                        // distance the way a real gobo projects. LOD 0 explicitly: screen-space
                        // derivatives inside a loop are undefined, and letting the hardware pick
                        // a mip here produces seams that swim with the camera.
                        float2 gxy = p.xy / max(lim, 1e-5);
                        gxy = float2(gxy.x * goboCos - gxy.y * goboSin,
                                     gxy.x * goboSin + gxy.y * goboCos);
                        dens *= SAMPLE_TEXTURE2D_LOD(_GoboTex, sampler_GoboTex,
                                                     gxy * goboK + 0.5, 0).r;
                    #endif

                    acc += dens * haze;
                    haze *= hazeStep;
                    t += dt;
                }

                // Riemann sum to an integral, expressed in METRES rather than object units, so
                // _Density means something per metre and the brightness does not change when the
                // proxy is resized. The column integrates in object units, so its intensity has
                // to be retuned every time its radius moves.
                acc *= dt / max(objectPerMetre, 1e-5);

                // --- shaping -----------------------------------------------------------------
                // Evaluated once, at the midpoint of the traversed span, rather than per sample.
                float tm = (t0 + t1) * 0.5;
                float3 mid = origin + dir * tm;

                float ph = _Phase * _DriftSpeed;
                float drift = 1.0 + _Drift * (sin(mid.z * 2.7 + ph) * 0.5 +
                                              sin(mid.x * 3.1 - ph * 1.3) * 0.3 +
                                              sin(mid.y * 2.3 + ph * 0.7) * 0.2);

                // Schlick's approximation to Henyey-Greenstein: a squared denominator instead of
                // a 1.5 power, visually indistinguishable at this density and much cheaper. It
                // is what makes a shaft flare when you look back up it and almost vanish when
                // you look across it -- the single strongest cue that there is light in the air
                // rather than a cone-shaped object hanging there.
                //
                // Measured in WORLD space. dirWS points camera -> fragment and the axis points
                // lamp -> floor, so looking back into the lamp gives cos = -1, which is where
                // the peak belongs.
                float3 dirWS = (IN.positionWS - GetCameraPositionWS()) / worldSpan;
                float cosT = dot(dirWS, normalize(IN.axisWS));
                float g = _Anisotropy;
                float denom = 1.0 + g * cosT;
                float phase = (1.0 - g * g) / max(denom * denom, 1e-4);

                float strength = acc * _Density * drift * phase * _Reveal * occlusion;

                // Gamma on the accumulated strength, which shapes how fast the whole beam fades
                // out without touching the analytic falloff.
                strength = pow(max(strength, 0.0), max(_EdgeSoftness, 0.05) * 2.0);

                // Rolled off, not clamped. URP-Performant runs m_SupportsHDR 0, so anything over
                // 1 is simply cut, and the long path you get on walking into the beam arrives as
                // a flat sheet with no form left in it. x/(1+x) climbs forever without reaching
                // 1 -- one divide, and the difference between standing in light and standing in
                // front of a wall.
                strength = strength / (1.0 + strength * _Rolloff);

                float3 rgb = _Color.rgb * _Intensity * strength;

                // Additive, so the air can only attenuate the beam -- never mix its colour into
                // it, which over a dark scene would paint haze in wherever the beam was
                // faintest. CompositeFogAttenuate carries that reasoning, and falls through to
                // Unity's own fog when no CompositeFogZone is driving the room.
                rgb = CompositeFogAttenuate(rgb, IN.positionWS, IN.fogCoord);

                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
