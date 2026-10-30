Shader "CompositeBody/MeltingMemory"
{
    Properties
    {
        [Header(Surface)]
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.1
        _WetSmoothness ("Wet Smoothness (melting areas)", Range(0, 1)) = 0.85
        _Seed ("Random Seed", Float) = 0

        [Header(Melt)]
        _MeltLength ("Melt Length (m)", Range(0, 1.5)) = 0.35
        _DripDensity ("Drips per Metre", Float) = 6
        _FlowSpeed ("Flow Speed", Float) = 0.25
        _StreakScale ("Streak Scale", Float) = 1.5

        [Header(Ripple)]
        _RippleAmp ("Ripple Amount (m)", Range(0, 0.05)) = 0.01
        _RippleFreq ("Ripple Frequency", Float) = 6
        _RippleSpeed ("Ripple Speed", Float) = 1

        [Header(Holes)]
        _Holes ("Hole Amount", Range(0, 1)) = 0.35
        _HoleScale ("Hole Scale (per metre)", Float) = 1.2
        _LipWidth ("Wet Lip Width", Range(0, 0.3)) = 0.06

        [Header(Dream Look)]
        _DreamTint ("Dream Tint", Color) = (0.86, 0.8, 0.95, 1)
        _DreamFade ("Dream Fade", Range(0, 1)) = 0.3
        _Chroma ("Colour Split (m)", Range(0, 0.03)) = 0.004
        _Iridescence ("Iridescent Sheen", Range(0, 2)) = 0.6
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        float4 _BaseColor;
        float _Smoothness;
        float _WetSmoothness;
        float _Seed;
        float _MeltLength;
        float _DripDensity;
        float _FlowSpeed;
        float _StreakScale;
        float _RippleAmp;
        float _RippleFreq;
        float _RippleSpeed;
        float _Holes;
        float _HoleScale;
        float _LipWidth;
        float4 _DreamTint;
        float _DreamFade;
        float _Chroma;
        float _Iridescence;
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

    // Pick the world plane the surface faces most, so the melt works on every wall face
    // without needing clean UVs. Returns 2D coords (metres) plus the two world axes they map to.
    float2 ProjectPlane(float3 posWS, float3 nWS, out float3 axisU, out float3 axisV)
    {
        float3 a = abs(nWS);
        if (a.x > a.y && a.x > a.z) { axisU = float3(0, 0, 1); axisV = float3(0, 1, 0); }
        else if (a.y > a.z)         { axisU = float3(1, 0, 0); axisV = float3(0, 0, 1); }
        else                        { axisU = float3(1, 0, 0); axisV = float3(0, 1, 0); }
        return float2(dot(posWS, axisU), dot(posWS, axisV));
    }

    struct Melt
    {
        float keep;     // >= 0 visible, < 0 clipped (distance-ish to a hole edge, noise units)
        float2 q;       // projected surface position, metres
        float2 source;  // where on the surface this pixel's image has flowed from, metres
        float2 across;  // in-plane axis perpendicular to "down", for the colour split
        float streak;   // 0..1 flowing streak intensity, used for wetness and sheen
    };

    // The surface reads as a memory slowly melting: the image drips downward in streaks,
    // trembles with a ripple, and holes open and drift where it has run thin.
    // Everything is a constant amount; only the noise moves.
    Melt EvaluateMelt(float3 posWS, float3 nWS)
    {
        Melt o;
        float3 axisU, axisV;
        float2 q = ProjectPlane(posWS, nWS, axisU, axisV);
        o.q = q;

        // "Up" within this face. Floors and ceilings have none, so they only ripple and hole.
        float2 up = float2(axisU.y, axisV.y);
        float vertical = saturate(length(up));
        up = vertical > 0.5 ? normalize(up) : float2(0, 1);
        float2 side = float2(up.y, -up.x);
        o.across = side;

        float across = dot(q, side) + _Seed * 7.0;
        float along = dot(q, up);
        float t = _Time.y;

        // Drip columns: thin per-drip variation on top of broad slow-changing regions.
        float thin = ValueNoise(float2(across * _DripDensity, t * _FlowSpeed * 0.3 + 3.1));
        float broad = ValueNoise(float2(across * _DripDensity * 0.2, along * 0.15 + t * _FlowSpeed * 0.1));
        float drip = pow(saturate(thin * 0.7 + broad * 0.6 - 0.2), 2.0);

        // Streak pattern that slides downward over time so the melt visibly flows.
        float streak = ValueNoise(float2(across * _DripDensity * 1.3, along * _StreakScale + t * _FlowSpeed));
        streak = smoothstep(0.35, 0.9, streak) * saturate(drip * 2.5 + 0.2);
        o.streak = streak * vertical;

        float pull = (drip + streak * 0.25) * _MeltLength * vertical;

        float2 ripple = float2(sin(q.y * _RippleFreq + t * _RippleSpeed * 2.0),
                               cos(q.x * _RippleFreq * 0.8 + t * _RippleSpeed * 1.6)) * _RippleAmp;

        // Content above flows down to here, so this pixel shows the image from higher up.
        o.source = q + up * pull + ripple;

        // Holes stretched along the flow, drifting slowly downward with it.
        float2 hq = float2(across * _HoleScale, (along + pull * 0.5) * _HoleScale * 0.35 + t * _FlowSpeed * 0.3);
        float h = ValueNoise(hq) * 0.6 + ValueNoise(hq * 2.1 + 17.3) * 0.4;
        o.keep = (1.0 - _Holes) - h;
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

            half4 frag(Varyings IN, bool isFront : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 geoN = normalize(IN.normalWS);
                Melt m = EvaluateMelt(IN.positionWS, geoN);

                // Derivatives taken before the discard, to map plane-space shifts onto mesh UVs.
                float2 dqx = ddx(m.q), dqy = ddy(m.q);
                float2 duvx = ddx(IN.uv), duvy = ddy(IN.uv);
                clip(m.keep);

                half3 N = isFront ? geoN : -geoN;
                half3 V = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                // Sample the flowed image, with the colour channels pulled slightly apart in the
                // streaks like a half-remembered picture.
                float2 baseUV = IN.uv + PlaneDeltaToUV(m.source - m.q, dqx, dqy, duvx, duvy);
                float2 split = PlaneDeltaToUV(m.across * _Chroma * (0.3 + m.streak), dqx, dqy, duvx, duvy);
                half3 albedo;
                albedo.r = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, baseUV + split, duvx, duvy).r;
                albedo.g = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, baseUV, duvx, duvy).g;
                albedo.b = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, baseUV - split, duvx, duvy).b;
                albedo *= _BaseColor.rgb;

                // Soft pastel haze, stronger where the image is running.
                half dream = saturate(_DreamFade + m.streak * 0.4);
                albedo = lerp(albedo, albedo * 0.7 + _DreamTint.rgb * 0.35, dream);

                // Wet, glossy lip around each hole, and wet streaks.
                half lip = 1.0 - smoothstep(0.0, _LipWidth + 1e-4, m.keep);
                half wet = saturate(lip + m.streak * 0.6);

                // Thin-film shimmer that shifts with viewing angle and the flowing streaks.
                half hue = frac(dot(N, V) * 1.5 + m.streak * 0.8 + lip * 0.5 + _Time.y * 0.05);
                half3 film = 0.5 + 0.5 * cos(TWO_PI * (hue + half3(0.0, 0.33, 0.67)));
                film = lerp(film, 1.0, 0.45); // keep it pastel
                half3 emission = film * (lip * 0.6 + m.streak * 0.25) * _Iridescence * 0.35;

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
                surface.smoothness = lerp(_Smoothness, _WetSmoothness, wet);
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
                clip(EvaluateMelt(IN.positionWS, normalize(IN.normalWS)).keep);
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
                clip(EvaluateMelt(IN.positionWS, normalize(IN.normalWS)).keep);
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
                clip(EvaluateMelt(IN.positionWS, n).keep);
                return half4(isFront ? n : -n, 0);
            }
            ENDHLSL
        }
    }
}
