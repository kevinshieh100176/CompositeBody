Shader "CompositeBody/SwirlFlow"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.3
        _Metallic ("Metallic", Range(0, 1)) = 0

        [Header(Swirl Centre)]
        _Center ("Centre (UV 0-1)", Vector) = (0.5, 0.5, 0, 0)
        _Radius ("Effect Radius", Range(0.05, 2)) = 0.6

        [Header(Concentric Twist)]
        _Rings ("Ring Count", Range(1, 40)) = 14
        _RingStep ("Extra Twist Per Ring (rad)", Range(-1, 1)) = 0.22
        _Twist ("Core Twist (rad)", Range(-20, 20)) = 7
        _CoreFalloff ("Core Falloff", Range(0.2, 8)) = 2.2
        _EdgeLine ("Ring Edge Line", Range(0, 1)) = 0.35
        _EdgeWidth ("Ring Edge Width", Range(0.001, 0.2)) = 0.025
        _EdgeColor ("Ring Edge Colour", Color) = (0.85, 0.9, 0.95, 1)

        [Header(Flow Warp)]
        _WarpScale ("Flow Scale", Float) = 3
        _WarpAmount ("Flow Amount", Range(0, 0.5)) = 0.08
        _Speed ("Rotation Speed", Float) = 0.15
        _FlowSpeed ("Flow Speed", Float) = 0.05
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fog
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _Smoothness;
                float _Metallic;
                float4 _Center;
                float _Radius;
                float _Rings;
                float _RingStep;
                float _Twist;
                float _CoreFalloff;
                float _EdgeLine;
                float _EdgeWidth;
                float4 _EdgeColor;
                float _WarpScale;
                float _WarpAmount;
                float _Speed;
                float _FlowSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 staticLightmapUV  : TEXCOORD1;
                float2 dynamicLightmapUV : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 4);
            #ifdef DYNAMICLIGHTMAP_ON
                float2 dynamicLightmapUV : TEXCOORD5;
            #endif
            #ifdef USE_APV_PROBE_OCCLUSION
                float4 probeOcclusion : TEXCOORD6;
            #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                // Carries which eye this vertex belongs to into the fragment stage. Both eyes
                // are drawn in one pass with instancing, so without this channel the fragment
                // stage shades whichever eye happened to be set last.
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x),
                            lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y);
            }

            float2 Rotate(float2 p, float a)
            {
                float s, c;
                sincos(a, s, c);
                return float2(c * p.x - s * p.y, s * p.x + c * p.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = IN.uv;
                OUT.fogFactor = ComputeFogFactor(p.positionCS.z);
                OUTPUT_LIGHTMAP_UV(IN.staticLightmapUV, unity_LightmapST, OUT.staticLightmapUV);
            #ifdef DYNAMICLIGHTMAP_ON
                OUT.dynamicLightmapUV = IN.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
            #endif
                OUTPUT_SH4(p.positionWS, OUT.normalWS, GetWorldSpaceNormalizeViewDir(p.positionWS), OUT.vertexSH, OUT.probeOcclusion);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // Work in a centred space where r = 1 is the edge of the effect.
                float2 p = (IN.uv - _Center.xy) / _Radius;
                float r = length(p);
                float inside = 1.0 - smoothstep(0.92, 1.0, r);

                // Concentric discs, each spun a little further than the last (the fan-of-cards look).
                float ringPos = r * _Rings;
                float ring = floor(ringPos);
                float ringFrac = frac(ringPos);
                float core = exp(-r * _CoreFalloff);
                float t = _Time.y;
                float angle = (ring * _RingStep + _Twist * core + t * _Speed * (1.0 + ring * 0.15)) * inside;
                float2 q = Rotate(p, angle);

                // Slow marbling flow so the streaks look liquid rather than purely geometric.
                float2 fp = q * _WarpScale + t * _FlowSpeed;
                float2 warp = float2(ValueNoise(fp) - 0.5, ValueNoise(fp + 19.7) - 0.5) * 2.0;
                q += warp * _WarpAmount * inside * (0.4 + 0.6 * smoothstep(0.0, 1.0, r));

                float2 uv = q * _Radius + _Center.xy;
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, TRANSFORM_TEX(uv, _BaseMap)).rgb * _BaseColor.rgb;

                // Thin seams where one disc meets the next.
                float edge = min(ringFrac, 1.0 - ringFrac) * _Rings / max(_Rings, 1.0);
                float seam = (1.0 - smoothstep(0.0, _EdgeWidth * _Rings * 0.2, edge)) * _EdgeLine * inside;
                albedo = lerp(albedo, _EdgeColor.rgb, saturate(seam));

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = normalize(IN.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord = IN.fogFactor;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                // Baked GI: same selection logic as URP/Lit (lightmap, dynamic lightmap, APV, or SH).
                #if defined(DYNAMICLIGHTMAP_ON)
                    inputData.bakedGI = SAMPLE_GI(IN.staticLightmapUV, IN.dynamicLightmapUV, IN.vertexSH, inputData.normalWS);
                    inputData.shadowMask = SAMPLE_SHADOWMASK(IN.staticLightmapUV);
                #elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
                    inputData.bakedGI = SAMPLE_GI(IN.vertexSH,
                        GetAbsolutePositionWS(inputData.positionWS),
                        inputData.normalWS,
                        inputData.viewDirectionWS,
                        IN.positionCS.xy,
                        IN.probeOcclusion,
                        inputData.shadowMask);
                #else
                    inputData.bakedGI = SAMPLE_GI(IN.staticLightmapUV, IN.vertexSH, inputData.normalWS);
                    inputData.shadowMask = SAMPLE_SHADOWMASK(IN.staticLightmapUV);
                #endif

                SurfaceData surf = (SurfaceData)0;
                surf.albedo = albedo;
                surf.metallic = _Metallic;
                surf.smoothness = _Smoothness;
                surf.occlusion = 1;
                surf.alpha = 1;
                surf.normalTS = half3(0, 0, 1);

                half4 color = UniversalFragmentPBR(inputData, surf);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
