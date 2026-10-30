Shader "CompositeBody/OverexposedMemory"
{
    Properties
    {
        [Header(Surface)]
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.1
        _Seed ("Random Seed", Float) = 0

        [Header(Fading)]
        _Fade ("Fade Amount", Range(0, 1)) = 0.5
        _FadeScale ("Fade Patch Scale (per metre)", Float) = 0.8
        _FadeSoftness ("Fade Softness", Range(0.02, 0.6)) = 0.25
        _DriftSpeed ("Drift Speed", Float) = 0.05
        _VanishStart ("Vanish Start (fade level)", Range(0, 1)) = 0.7

        [Header(Exposure)]
        _BleachColor ("Bleach Colour", Color) = (1, 0.96, 0.88, 1)
        _Bleach ("Bleach", Range(0, 1)) = 0.75
        _Glow ("Overexposure Glow", Range(0, 3)) = 0.6
        _Blur ("Blur (m)", Range(0, 0.1)) = 0.03

        [Header(Double Exposure)]
        _EchoDistance ("Echo Distance (m)", Range(0, 0.4)) = 0.08
        _EchoStrength ("Echo Strength", Range(0, 1)) = 0.5
        _EchoSpeed ("Echo Drift Speed", Float) = 0.3

        [Header(Film)]
        _LeakColorA ("Light Leak A", Color) = (1, 0.45, 0.15, 1)
        _LeakColorB ("Light Leak B", Color) = (0.95, 0.3, 0.55, 1)
        _LightLeak ("Light Leak Strength", Range(0, 2)) = 0.35
        _Grain ("Film Grain", Range(0, 0.3)) = 0.06
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        float4 _BaseColor;
        float _Smoothness;
        float _Seed;
        float _Fade;
        float _FadeScale;
        float _FadeSoftness;
        float _DriftSpeed;
        float _VanishStart;
        float4 _BleachColor;
        float _Bleach;
        float _Glow;
        float _Blur;
        float _EchoDistance;
        float _EchoStrength;
        float _EchoSpeed;
        float4 _LeakColorA;
        float4 _LeakColorB;
        float _LightLeak;
        float _Grain;
    CBUFFER_END

    float Hash21(float2 p)
    {
        p = frac(p * float2(123.34, 456.21));
        p += dot(p, p + 45.32);
        return frac(p.x * p.y);
    }

    float ValueNoise(float2 p)
    {
        float2 i = floor(p);
        float2 f = frac(p);
        f = f * f * (3.0 - 2.0 * f);
        return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x),
                    lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y);
    }

    // Stable per-pixel dither for the soft vanish (interleaved gradient noise).
    float Dither(float2 pixel)
    {
        return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
    }

    // Pick the world plane the surface faces most, so the effect works on every wall face
    // without needing clean UVs. Returns 2D coords (metres) plus the two world axes they map to.
    float2 ProjectPlane(float3 posWS, float3 nWS, out float3 axisU, out float3 axisV)
    {
        float3 a = abs(nWS);
        if (a.x > a.y && a.x > a.z) { axisU = float3(0, 0, 1); axisV = float3(0, 1, 0); }
        else if (a.y > a.z)         { axisU = float3(1, 0, 0); axisV = float3(0, 0, 1); }
        else                        { axisU = float3(1, 0, 0); axisV = float3(0, 1, 0); }
        return float2(dot(posWS, axisU), dot(posWS, axisV));
    }

    struct Exposure
    {
        float keep;   // >= 0 visible, < 0 clipped
        float fade;   // 0 = clear memory, 1 = washed out to light
        float vanish; // 0..1 how far a washed-out patch has dissolved
        float2 q;     // projected surface position, metres
    };

    // Patches of the surface wash out into light, as if overexposed. The amount is constant;
    // two noise layers drift at different rates so the patches wander and change shape.
    Exposure EvaluateExposure(float3 posWS, float3 nWS, float2 pixel)
    {
        Exposure o;
        float3 axisU, axisV;
        float2 q = ProjectPlane(posWS, nWS, axisU, axisV);
        o.q = q;

        float t = _Time.y * _DriftSpeed;
        float2 nq = q * _FadeScale + _Seed * 3.7;
        float n = ValueNoise(nq + float2(t, t * 0.6)) * 0.65
                + ValueNoise(nq * 2.3 - float2(t * 0.7, -t * 0.4) + 13.0) * 0.35;

        float edge = 1.0 - _Fade;
        o.fade = smoothstep(edge - _FadeSoftness, edge + _FadeSoftness, n);

        // The most washed-out part dissolves away softly through a dither, like light burning through.
        o.vanish = saturate((o.fade - _VanishStart) / max(1.0 - _VanishStart, 1e-4));
        o.keep = (1.0 - o.vanish) - Dither(pixel) * 0.999 - 1e-4;
        return o;
    }

    // Converts a shift in plane space (metres) into the matching shift in mesh UV, using the
    // screen-space derivatives of both.
    float2 PlaneDeltaToUV(float2 dPlane, float2 dqx, float2 dqy, float2 duvx, float2 duvy)
    {
        float det = dqx.x * dqy.y - dqy.x * dqx.y;
        if (abs(det) < 1e-10) return 0;
        float a = (dPlane.x * dqy.y - dPlane.y * dqy.x) / det;
        float b = (dqx.x * dPlane.y - dqx.y * dPlane.x) / det;
        return a * duvx + b * duvy;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 4);
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor = ComputeFogFactor(p.positionCS.z);
                OUTPUT_LIGHTMAP_UV(IN.uv1, unity_LightmapST, OUT.lightmapUV);
                return OUT;
            }

            half3 SampleBase(float2 uv, float2 duvx, float2 duvy)
            {
                return SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, uv, duvx, duvy).rgb;
            }

            half4 frag(Varyings IN, bool isFront : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 geoN = normalize(IN.normalWS);
                Exposure e = EvaluateExposure(IN.positionWS, geoN, IN.positionCS.xy);

                // Derivatives taken before the discard, to map plane-space shifts onto mesh UVs.
                float2 dqx = ddx(e.q), dqy = ddy(e.q);
                float2 duvx = ddx(IN.uv), duvy = ddy(IN.uv);
                clip(e.keep);

                half3 N = isFront ? geoN : -geoN;
                half3 V = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                float f = e.fade;
                float t = _Time.y;

                // Blur grows as the memory washes out: four taps around the centre on a slowly turning cross.
                float2 du = PlaneDeltaToUV(float2(1, 0), dqx, dqy, duvx, duvy);
                float2 dv = PlaneDeltaToUV(float2(0, 1), dqx, dqy, duvx, duvy);
                float r = _Blur * f;
                float sn, cs;
                sincos(t * 0.2, sn, cs);
                float2 a = (du * cs + dv * sn) * r;
                float2 b = (dv * cs - du * sn) * r;
                half3 albedo = SampleBase(IN.uv, duvx, duvy) * 0.4
                             + (SampleBase(IN.uv + a, duvx, duvy) + SampleBase(IN.uv - a, duvx, duvy)
                              + SampleBase(IN.uv + b, duvx, duvy) + SampleBase(IN.uv - b, duvx, duvy)) * 0.15;

                // Double exposure: a drifting ghost of the same surface laid over the faded patches.
                float2 echoDir = float2(sin(t * _EchoSpeed), cos(t * _EchoSpeed * 0.73));
                float2 echoUV = IN.uv + (du * echoDir.x + dv * echoDir.y) * _EchoDistance;
                half3 echo = SampleBase(echoUV, duvx, duvy);
                albedo = lerp(albedo, max(albedo, echo), _EchoStrength * f);
                albedo = lerp(albedo, (albedo + echo) * 0.5, _EchoStrength * f * 0.5);
                albedo *= _BaseColor.rgb;

                // Wash out: lose colour first, then bleach toward warm white.
                half luma = dot(albedo, half3(0.299, 0.587, 0.114));
                albedo = lerp(albedo, luma.xxx, f * 0.8);
                albedo = lerp(albedo, _BleachColor.rgb, f * _Bleach);

                // Film grain, refreshed every frame.
                half grain = Hash21(IN.positionCS.xy + frac(t * 7.31) * 113.0) - 0.5;
                albedo = saturate(albedo * (1.0 + grain * _Grain * 2.0));

                // Overexposed light bleeding out of the faded patches, brightest where they dissolve.
                half3 emission = _BleachColor.rgb * _Glow * (f * f + e.vanish * 1.5);

                // Slow warm light leaks drifting across the wall.
                float leak = ValueNoise(e.q * 0.25 + float2(t * 0.03, -t * 0.02) + _Seed);
                half3 leakCol = lerp(_LeakColorA.rgb, _LeakColorB.rgb, ValueNoise(e.q * 0.2 - t * 0.02 + 7.0));
                emission += leakCol * smoothstep(0.55, 0.9, leak) * _LightLeak;

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = N;
                inputData.viewDirectionWS = V;
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord = IN.fogFactor;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                #if defined(LIGHTMAP_ON)
                    inputData.bakedGI = SampleLightmap(IN.lightmapUV, float2(0, 0), N);
                    inputData.shadowMask = SAMPLE_SHADOWMASK(IN.lightmapUV);
                #else
                    inputData.bakedGI = SampleSH(N);
                    inputData.shadowMask = half4(1, 1, 1, 1);
                #endif

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.smoothness = _Smoothness;
                surface.occlusion = 1;
                surface.normalTS = half3(0, 0, 1);
                surface.emission = emission;
                surface.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, IN.fogFactor);
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(IN.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - posWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 posCS = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, lightDir));
                #if UNITY_REVERSED_Z
                    posCS.z = min(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    posCS.z = max(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = posCS;
                OUT.positionWS = posWS;
                OUT.normalWS = nWS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                clip(EvaluateExposure(IN.positionWS, normalize(IN.normalWS), IN.positionCS.xy).keep);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                clip(EvaluateExposure(IN.positionWS, normalize(IN.normalWS), IN.positionCS.xy).keep);
                return IN.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN, bool isFront : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 n = normalize(IN.normalWS);
                clip(EvaluateExposure(IN.positionWS, n, IN.positionCS.xy).keep);
                return half4(isFront ? n : -n, 0);
            }
            ENDHLSL
        }
    }
}
