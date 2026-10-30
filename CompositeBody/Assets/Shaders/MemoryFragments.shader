Shader "CompositeBody/MemoryFragments"
{
    Properties
    {
        [Header(Surface)]
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.1

        [Header(Particles)]
        _ParticleScale ("Particles per Metre", Float) = 28
        _Seed ("Random Seed", Float) = 0
        _WindDir ("Wind Direction (world)", Vector) = (1, 0.35, 0.6, 0)
        _Scatter ("Scatter Distance (particles)", Range(0, 12)) = 8
        _Spread ("Sideways Spread", Range(0, 1)) = 0.6
        _Stretch ("Motion Stretch", Range(0, 4)) = 1.2
        _Flutter ("Flutter", Range(0, 0.5)) = 0.2
        _FlutterSpeed ("Flutter Speed", Float) = 2

        [Header(Dispersal)]
        _Progress ("Progress (amount forgotten)", Range(0, 1)) = 0.5
        _NoiseSpeed ("Noise Animation Speed", Float) = 0.15
        _Softness ("Gradient Softness", Range(0.02, 1)) = 0.4
        _ClusterScale ("Cluster Noise Scale", Float) = 0.6
        _Randomness ("Per-Particle Randomness", Range(0, 1)) = 0.55
        _Unstable ("Particle Instability", Range(0, 0.3)) = 0.05
        _UnstableSpeed ("Instability Speed", Float) = 1.3

        [Header(Memory Look)]
        _ParticleShade ("Loose Particle Shade", Color) = (0.62, 0.57, 0.52, 1)
        _MemoryTint ("Fading Tint", Color) = (0.82, 0.74, 0.6, 1)
        _Fade ("Fade Toward Tint", Range(0, 1)) = 0.35
        _BaseFade ("Base Desaturation", Range(0, 1)) = 0.1
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    // How many particle-widths upwind each pixel searches for a particle that blew onto it.
    // Scatter is faded out before it reaches this, so raising Scatter past ~12 needs more steps.
    #define DISPERSE_STEPS 14

    TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        float4 _BaseColor;
        float _Smoothness;
        float _ParticleScale;
        float _Seed;
        float4 _WindDir;
        float _Scatter;
        float _Spread;
        float _Stretch;
        float _Flutter;
        float _FlutterSpeed;
        float _Progress;
        float _NoiseSpeed;
        float _Softness;
        float _ClusterScale;
        float _Randomness;
        float _Unstable;
        float _UnstableSpeed;
        float4 _ParticleShade;
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

    // Pick the world plane the surface faces most, so particles stay square on every wall face
    // without needing clean UVs. Returns 2D coords plus the two world axes they map to.
    float2 ProjectPlane(float3 posWS, float3 nWS, out float3 axisU, out float3 axisV)
    {
        float3 a = abs(nWS);
        if (a.x > a.y && a.x > a.z) { axisU = float3(0, 0, 1); axisV = float3(0, 1, 0); }
        else if (a.y > a.z)         { axisU = float3(1, 0, 0); axisV = float3(0, 0, 1); }
        else                        { axisU = float3(1, 0, 0); axisV = float3(0, 1, 0); }
        return float2(dot(posWS, axisU), dot(posWS, axisV));
    }

    struct Particle
    {
        float keep;     // >= 0 visible, < 0 clipped
        float2 offset;  // how far the visible particle has blown from home, particle units
        float dissolve; // 0 = still part of the wall, 1 = gone
        float rnd;
    };

    Particle EvaluateParticles(float3 posWS, float3 nWS)
    {
        Particle o;
        o.keep = -1.0;
        o.offset = 0;
        o.dissolve = 1.0;
        o.rnd = 0;

        float3 axisU, axisV;
        float2 q = ProjectPlane(posWS, nWS, axisU, axisV) * _ParticleScale;

        float2 wind = float2(dot(_WindDir.xyz, axisU), dot(_WindDir.xyz, axisV));
        wind = dot(wind, wind) > 1e-4 ? normalize(wind) : float2(0, 1);
        float2 perp = float2(-wind.y, wind.x);
        float s = _Softness;
        // The amount dispersed stays fixed at Progress; only the pattern moves. Two noise layers
        // scroll downwind at different rates so the pattern evolves rather than just sliding.
        float t = _Time.y * _NoiseSpeed;
        float2 scrollA = -wind * t;
        float2 scrollB = -wind * t * 1.7 + perp * t * 0.6 + 21.4;

        // Each particle lives on a grid cell and blows downwind by an amount set by how far
        // dissolved it is. To draw it, walk upwind from this pixel and test the cells whose
        // particle could have landed here; the first hit (least travelled) wins.
        [loop] for (int k = 0; k < DISPERSE_STEPS; k++)
        {
            float2 base = floor(q - wind * k - 0.5);
            [unroll] for (int n = 0; n < 4; n++)
            {
                float2 cell = base + float2(n & 1, n >> 1);
                float2 centre = cell + 0.5;
                float rnd = Hash21(cell + _Seed);
                float2 r2 = Hash22(cell + _Seed + 7.3);

                // Order of dispersal: clustered noise mixed with per-particle randomness, evenly over the whole surface.
                float2 nq = centre / _ParticleScale * _ClusterScale * 4.0 + _Seed;
                float cluster = ValueNoise(nq + scrollA) * 0.65 + ValueNoise(nq * 2.3 + scrollB) * 0.35;
                float threshold = lerp(cluster, rnd, _Randomness);
                threshold += sin(_Time.y * _UnstableSpeed + rnd * TWO_PI) * _Unstable;
                float d = saturate((_Progress * (1.0 + s + _Unstable) - threshold) / s);
                if (d <= 0.0 && k > 0) continue; // a resting particle only covers its own cell

                // Loose particles accelerate away, drift sideways and flutter.
                float travel = d * d * _Scatter * (0.4 + r2.x);
                float flutter = sin(_Time.y * _FlutterSpeed + rnd * TWO_PI) * _Flutter;
                float2 off = wind * travel + perp * ((r2.y - 0.5) * _Spread + flutter) * d;

                // Particle shape in its own frame: smeared along the wind, then tumbled.
                float2 local = q - off - centre;
                float along = dot(local, wind);
                local -= wind * along * (d * _Stretch / (1.0 + d * _Stretch));
                float ang = (rnd - 0.5) * TWO_PI * saturate(d * 2.0);
                float sn, cs;
                sincos(ang, sn, cs);
                local = float2(cs * local.x - sn * local.y, sn * local.x + cs * local.y);
                float square = max(abs(local.x), abs(local.y));
                float shape = lerp(square, (abs(local.x) + abs(local.y)) * 0.75, saturate(d * 2.0) * r2.x);

                // Full-size squares tile the wall seamlessly; loose ones shrink to flakes and dust,
                // and vanish before they outrun the search.
                float size = 0.5 * lerp(1.0, 0.25 + 0.55 * r2.y, saturate(d * 3.0));
                size *= 1.0 - smoothstep(0.8, 1.0, d);
                size *= 1.0 - smoothstep(DISPERSE_STEPS * 0.7, DISPERSE_STEPS * 0.95, travel);

                if (shape <= size + 1e-4)
                {
                    o.keep = size - shape + 1e-4;
                    o.offset = off;
                    o.dissolve = d;
                    o.rnd = rnd;
                    return o;
                }
            }
        }
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

                // Screen derivatives taken before any discard: they give the local mapping from
                // particle-grid space to mesh UV, so a blown particle can show the patch of wall it came from.
                float3 axisU, axisV;
                float2 q = ProjectPlane(IN.positionWS, geoN, axisU, axisV) * _ParticleScale;
                float2 dqx = ddx(q), dqy = ddy(q);
                float2 duvx = ddx(IN.uv), duvy = ddy(IN.uv);

                Particle pt = EvaluateParticles(IN.positionWS, geoN);
                clip(pt.keep);

                float det = dqx.x * dqy.y - dqy.x * dqx.y;
                float2 homeUV = IN.uv;
                if (abs(det) > 1e-8)
                {
                    // Solve offset = a*dqx + b*dqy, then apply the same a, b to the UV derivatives.
                    float a = (pt.offset.x * dqy.y - pt.offset.y * dqy.x) / det;
                    float b = (dqx.x * pt.offset.y - dqx.y * pt.offset.x) / det;
                    homeUV -= a * duvx + b * duvy;
                }

                half3 N = isFront ? geoN : -geoN;
                half3 V = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                float d = pt.dissolve;

                half3 albedo = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, homeUV, duvx, duvy).rgb * _BaseColor.rgb;
                half luma = dot(albedo, half3(0.299, 0.587, 0.114));
                albedo = lerp(albedo, luma.xxx, _BaseFade);
                albedo = lerp(albedo, luma * _MemoryTint.rgb * 1.4, saturate(d * _Fade));
                // Loose flakes read darker and dustier than the wall they left, varied per flake.
                albedo *= lerp(1.0, _ParticleShade.rgb * (0.8 + 0.4 * pt.rnd), saturate(d * 2.0));

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
                clip(EvaluateParticles(IN.positionWS, normalize(IN.normalWS)).keep);
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
                clip(EvaluateParticles(IN.positionWS, normalize(IN.normalWS)).keep);
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
                clip(EvaluateParticles(IN.positionWS, n).keep);
                return half4(isFront ? n : -n, 0);
            }
            ENDHLSL
        }
    }
}
