Shader "CompositeBody/RicePaper"
{
    Properties
    {
        [Header(Paper)]
        _BaseColor ("Paper Colour", Color) = (0.93, 0.9, 0.8, 1)
        [NoScaleOffset] _BaseMap ("Paper Texture (optional)", 2D) = "white" {}
        _FibreScale ("Fibre Scale", Float) = 180
        _FibreStrength ("Fibre Strength", Range(0, 1)) = 0.35
        _Smoothness ("Smoothness", Range(0, 1)) = 0.08

        [Header(Translucency)]
        _TransColor ("Transmitted Light Tint", Color) = (1.0, 0.82, 0.55, 1)
        _TransIntensity ("Transmission Intensity", Range(0, 8)) = 2.5
        _Wrap ("Light Wrap (soft scatter)", Range(0, 1)) = 0.35
        _Thickness ("Thickness (lower = more glow)", Range(0, 1)) = 0.25
        _GlowSelf ("Ambient Glow", Range(0, 2)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _FibreScale;
                float _FibreStrength;
                float _Smoothness;
                float4 _TransColor;
                float _TransIntensity;
                float _Wrap;
                float _Thickness;
                float _GlowSelf;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
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

            // Cheap fibre field: stretched noise at two angles, so the paper looks laid up from
            // crossing strands. Returns ~0..1; thin spots read as brighter when backlit.
            float Fibres(float2 uv)
            {
                float2 a = uv * _FibreScale * float2(1.0, 0.18);
                float2 b = float2(uv.y, uv.x) * _FibreScale * float2(1.0, 0.22) + 17.3;
                return saturate(ValueNoise(a) * 0.55 + ValueNoise(b) * 0.45
                              + ValueNoise(uv * _FibreScale * 0.15) * 0.25 - 0.1);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = IN.uv;
                OUT.fogFactor = ComputeFogFactor(p.positionCS.z);
                return OUT;
            }

            // Front: wrapped diffuse. Back: light arriving from behind the sheet, tinted and
            // attenuated by thickness. Shadows apply to the lit side only -- the sheet itself
            // would otherwise shadow its own transmitted light.
            half3 Shade(Light l, half3 N, half3 V, half3 albedo, half thinMask, bool useShadow)
            {
                half ndl = dot(N, l.direction);
                half wrap = _Wrap;
                half front = saturate((ndl + wrap) / (1.0 + wrap));
                half back = saturate((-ndl + wrap) / (1.0 + wrap));

                half atten = l.distanceAttenuation * (useShadow ? l.shadowAttenuation : 1.0);
                half transAtten = l.distanceAttenuation;

                // Forward scatter: brightest when looking toward the light through the sheet.
                half vdl = saturate(dot(-V, l.direction));
                half scatter = 0.6 + 0.4 * pow(vdl, 3.0);

                half3 diffuse = albedo * front * atten;
                half3 trans = _TransColor.rgb * albedo * back * scatter * transAtten
                              * _TransIntensity * lerp(1.0, thinMask, 0.8) * (1.0 - _Thickness);
                return (diffuse + trans) * l.color;
            }

            half4 frag(Varyings IN, bool isFront : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half3 N = normalize(IN.normalWS);
                // Always shade the face that is looking at us; the "other side" is handled by the transmission term.
                N = isFront ? N : -N;
                half3 V = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                half fib = Fibres(IN.uv);
                half3 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb;
                half3 albedo = _BaseColor.rgb * tex * lerp(1.0, 0.75 + 0.5 * fib, _FibreStrength);
                half thinMask = lerp(1.0, 0.6 + 0.8 * fib, _FibreStrength);

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half3 color = Shade(mainLight, N, V, albedo, thinMask, false);

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = N;
                inputData.viewDirectionWS = V;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                #if defined(_ADDITIONAL_LIGHTS)
                    uint pixelLightCount = GetAdditionalLightsCount();
                    #if USE_FORWARD_PLUS
                        [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                        {
                            FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
                            Light l = GetAdditionalLight(lightIndex, IN.positionWS, half4(1, 1, 1, 1));
                            color += Shade(l, N, V, albedo, thinMask, false);
                        }
                    #endif
                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light l = GetAdditionalLight(lightIndex, IN.positionWS, half4(1, 1, 1, 1));
                        color += Shade(l, N, V, albedo, thinMask, false);
                    LIGHT_LOOP_END
                #endif

                // Baked / ambient light, also scattered through the sheet so unlit paper never goes black.
                half3 ambient = SampleSH(N) * albedo;
                half3 glow = albedo * _TransColor.rgb * _GlowSelf * thinMask;
                color += ambient + glow;

                // Soft sheen on the surface.
                half3 H = normalize(mainLight.direction + V);
                half spec = pow(saturate(dot(N, H)), lerp(8.0, 128.0, _Smoothness)) * _Smoothness * 0.5;
                color += spec * mainLight.color * mainLight.shadowAttenuation;

                color = MixFog(color, IN.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
