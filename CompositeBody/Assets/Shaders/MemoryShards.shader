Shader "CompositeBody/MemoryShards"
{
    Properties
    {
        [Header(Surface)]
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.1

        [Header(Fragments)]
        _CellScale ("Fragments per Metre", Float) = 3.5
        _BlendSharpness ("Triplanar Blend Sharpness", Range(1, 32)) = 8
        _Seed ("Random Seed", Float) = 0
        _CrackWidth ("Crack Line Width", Range(0, 0.2)) = 0.025
        _CrackColor ("Crack Colour", Color) = (0.25, 0.22, 0.2, 1)

        [Header(Disintegration)]
        _Progress ("Progress (amount forgotten)", Range(0, 1)) = 0.5
        _NoiseSpeed ("Noise Animation Speed", Float) = 0.15
        _Softness ("Softness",Range(0.02, 1)) = 0.25
        _ClusterScale ("Cluster Noise Scale", Float) = 0.35
        _Randomness ("Per-Shard Randomness", Range(0, 1)) = 0.45
        _Unstable ("Shard Instability", Range(0, 0.3)) = 0.06
        _UnstableSpeed ("Instability Speed", Float) = 1.3

        [Header(Fragment Motion)]
        _Shrink ("Shard Shrink", Range(0, 1)) = 0.4
        _Drift ("Image Drift (inside shard)", Range(0, 1)) = 0.35

        [Header(Memory Look)]
        [HDR] _EdgeColor ("Edge Glow", Color) = (0.9, 0.62, 0.35, 1)
        _EdgeWidth ("Edge Glow Width", Range(0, 0.3)) = 0.03
        _MemoryTint ("Fading Tint", Color) = (0.86, 0.78, 0.66, 1)
        _Fade ("Fade Toward Tint", Range(0, 1)) = 0.85
        _BaseFade ("Base Desaturation", Range(0, 1)) = 0.15
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        float4 _BaseColor;
        float _Smoothness;
        float _CellScale;
        float _BlendSharpness;
        float _Seed;
        float _CrackWidth;
        float4 _CrackColor;
        float _Progress;
        float _NoiseSpeed;
        float _Softness;
        float _ClusterScale;
        float _Randomness;
        float _Unstable;
        float _UnstableSpeed;
        float _Shrink;
        float _Drift;
        float4 _EdgeColor;
        float _EdgeWidth;
        float4 _MemoryTint;
        float _Fade;
        float _BaseFade;
    CBUFFER_END

    float Hash21(float2 p)
    {
        p = frac(p * float2(123.34, 456.21));
        p += dot(p, p + 45.32);
        return frac(p.x * p.y);
    }

    float2 Hash22(float2 p)
    {
        float n = Hash21(p);
        return float2(n, Hash21(p + n * 17.17));
    }

    float ValueNoise(float2 p)
    {
        float2 i = floor(p);
        float2 f = frac(p);
        f = f * f * (3.0 - 2.0 * f);
        return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x),
                    lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y);
    }

    // Triplanar weights for the three world planes (X-, Y-, Z-facing), so shards stay square-ish on
    // every face without needing clean UVs, and curved surfaces blend between planes instead of
    // snapping. Higher sharpness narrows the blend band so shards stay crisp away from edges.
    float3 TriplanarWeights(float3 nWS)
    {
        float3 w = pow(abs(nWS), _BlendSharpness);
        return w / (w.x + w.y + w.z);
    }

    // Voronoi with distance to the nearest cell border (IQ). cellId = lattice id of the shard,
    // toSeed = offset from this pixel to the shard's seed, in cell units.
    float VoronoiBorder(float2 x, out float2 cellId, out float2 toSeed)
    {
        float2 n = floor(x);
        float2 f = frac(x);
        float2 mg = 0, mr = 0;
        float md = 8.0;

        [unroll] for (int j = -1; j <= 1; j++)
        [unroll] for (int i = -1; i <= 1; i++)
        {
            float2 g = float2(i, j);
            float2 r = g + Hash22(n + g + _Seed) - f;
            float d = dot(r, r);
            if (d < md) { md = d; mr = r; mg = g; }
        }

        md = 8.0;
        [unroll] for (int j2 = -1; j2 <= 1; j2++)
        [unroll] for (int i2 = -1; i2 <= 1; i2++)
        {
            float2 g = mg + float2(i2, j2);
            float2 r = g + Hash22(n + g + _Seed) - f;
            float2 diff = r - mr;
            if (dot(diff, diff) > 1e-5)
                md = min(md, dot(0.5 * (mr + r), normalize(diff)));
        }

        cellId = n + mg;
        toSeed = mr;
        return md;
    }

    struct Fragment
    {
        float border;   // distance to shard edge, cell units
        float dissolve; // 0 = intact, 1 = gone
        float keep;     // >= 0 visible, < 0 clipped
        float2 drift;   // image slip inside the shard, before Drift scaling
    };

    // How far a shard's edge has receded toward its seed, in cell units. Shards shrink by Shrink
    // while loosening, then always close down to nothing by the end, so none ever vanish at full size.
    float ShrinkEdge(float dissolve)
    {
        return dissolve * _Shrink * 0.5 + smoothstep(0.5, 1.0, dissolve) * 0.75;
    }

    // Shard pattern on a single projection plane; planePos is in metres.
    Fragment EvaluatePlane(float2 planePos)
    {
        Fragment o;
        float2 p = planePos * _CellScale;

        float2 cellId, toSeed;
        o.border = VoronoiBorder(p, cellId, toSeed);
        float rnd = Hash21(cellId * 1.37 + _Seed + 3.1);

        // Order of forgetting: clustered noise mixed with per-shard randomness, evenly over the
        // whole surface, evaluated per shard so every shard leaves as one piece.
        // The amount forgotten stays fixed at Progress; only the pattern moves. Two noise layers
        // drift in different directions so the pattern evolves rather than just sliding.
        float t = _Time.y * _NoiseSpeed;
        float2 nq = cellId / _CellScale * _ClusterScale * 4.0 + _Seed;
        float cluster = ValueNoise(nq + float2(t, t * 0.6)) * 0.65
                      + ValueNoise(nq * 2.3 - float2(t * 0.7, -t * 0.4) + 13.0) * 0.35;
        float threshold = lerp(cluster, rnd, _Randomness);
        threshold += sin(_Time.y * _UnstableSpeed + rnd * TWO_PI) * _Unstable;

        float s = _Softness;
        o.dissolve = saturate((_Progress * (1.0 + s + _Unstable) - threshold) / s);

        o.keep = o.border - ShrinkEdge(o.dissolve);
        o.drift = (Hash22(cellId + 9.7) - 0.5) * o.dissolve;
        return o;
    }

    void AccumulateFragment(inout Fragment o, Fragment f, float w)
    {
        o.border   += f.border * w;
        o.dissolve += f.dissolve * w;
        o.keep     += f.keep * w;
        o.drift    += f.drift * w;
    }

    // Triplanar blend of the shard pattern. Planes with negligible weight are skipped, so flat
    // faces only pay for one Voronoi evaluation. Axis orientation matches the old single-plane pick.
    Fragment EvaluateFragment(float3 posWS, float3 nWS)
    {
        float3 w = TriplanarWeights(nWS);
        Fragment o = (Fragment)0;
        [branch] if (w.x > 0.001) AccumulateFragment(o, EvaluatePlane(posWS.zy), w.x);
        [branch] if (w.y > 0.001) AccumulateFragment(o, EvaluatePlane(posWS.xz), w.y);
        [branch] if (w.z > 0.001) AccumulateFragment(o, EvaluatePlane(posWS.xy), w.z);
        return o;
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
                Fragment fr = EvaluateFragment(IN.positionWS, geoN);
                clip(fr.keep);

                half3 N = isFront ? geoN : -geoN;
                half3 V = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                float d = fr.dissolve;

                // The picture inside a loosening shard slips sideways, like a misremembered detail.
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv + fr.drift * _Drift).rgb * _BaseColor.rgb;

                half luma = dot(albedo, half3(0.299, 0.587, 0.114));
                albedo = lerp(albedo, luma.xxx, _BaseFade);
                albedo = lerp(albedo, luma * _MemoryTint.rgb * 1.4, saturate(d * _Fade));

                // Hairline cracks are always present: the memory is already in pieces.
                half crack = smoothstep(0.0, _CrackWidth + 1e-4, fr.border);
                albedo *= lerp(_CrackColor.rgb, 1.0, crack);

                // Glowing rim along the receding edge of each dissolving shard.
                float shrinkEdge = ShrinkEdge(d);
                // Fades out again as the shard nears the end so small shards don't become solid glow.
                half rim = (1.0 - smoothstep(shrinkEdge, shrinkEdge + _EdgeWidth + 1e-4, fr.border))
                         * saturate(d * 4.0) * (1.0 - d * 0.7);
                half3 emission = _EdgeColor.rgb * rim;

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
                clip(EvaluateFragment(IN.positionWS, normalize(IN.normalWS)).keep);
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
                clip(EvaluateFragment(IN.positionWS, normalize(IN.normalWS)).keep);
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
                clip(EvaluateFragment(IN.positionWS, n).keep);
                return half4(isFront ? n : -n, 0);
            }
            ENDHLSL
        }
    }
}
