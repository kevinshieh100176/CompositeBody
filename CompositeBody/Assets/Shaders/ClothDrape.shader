Shader "CompositeBody/ClothDrape"
{
    Properties
    {
        [Header(Base Cloth)]
        _BaseColor ("Base Color", Color) = (0.014, 0.014, 0.016, 1)
        _DiffuseWrap ("Diffuse Wrap", Range(0, 1)) = 0.35

        [Header(Satin Sheen)]
        _SheenColor ("Sheen Color", Color) = (0.30, 0.31, 0.35, 1)
        _SheenRoughness ("Sheen Roughness", Range(0.05, 1)) = 0.48
        _SheenIntensity ("Sheen Intensity", Range(0, 4)) = 1.35

        [Header(Anisotropic Silk Highlight)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.50
        _Anisotropy ("Anisotropy", Range(-1, 1)) = 0.62
        _SpecColor2 ("Specular Color", Color) = (0.85, 0.86, 0.90, 1)
        _SpecIntensity ("Specular Intensity", Range(0, 4)) = 0.28
        _RoughnessBreakup ("Roughness Breakup", Range(0, 1)) = 0.55

        [Header(Grazing Rim)]
        _FresnelColor ("Fresnel Color", Color) = (0.42, 0.45, 0.50, 1)
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3.6
        _FresnelIntensity ("Fresnel Intensity", Range(0, 3)) = 0.32

        [Header(Micro Wrinkles)]
        _WrinkleStrength ("Wrinkle Strength", Range(0, 1)) = 0.45
        _WrinkleScale ("Wrinkle Scale", Float) = 34
        _WrinkleStretch ("Wrinkle Vertical Stretch", Range(1, 16)) = 7
        [Toggle] _WrinkleCylindrical ("Cylindrical Wrinkles (seamless on closed garments)", Float) = 0

        [Header(Ambient)]
        _AmbientIntensity ("Ambient Intensity", Range(0, 2)) = 0.38
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
            #pragma target 3.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _DiffuseWrap;
                float4 _SheenColor;
                float _SheenRoughness;
                float _SheenIntensity;
                float _Smoothness;
                float _Anisotropy;
                float4 _SpecColor2;
                float _SpecIntensity;
                float _RoughnessBreakup;
                float4 _FresnelColor;
                float _FresnelPower;
                float _FresnelIntensity;
                float _WrinkleStrength;
                float _WrinkleScale;
                float _WrinkleStretch;
                float _WrinkleCylindrical;
                float _AmbientIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 tangentWS   : TEXCOORD2;
                float3 bitangentWS : TEXCOORD3;
                float2 uv          : TEXCOORD4;
                float  fogCoord    : TEXCOORD5;
                float3 positionOS  : TEXCOORD6;
            };

            // ---- Procedural micro-wrinkle field -------------------------------------------
            // Cheap value noise; the fragment stage takes finite differences of this to bend
            // the shading normal, so the silk picks up fine creases without a normal map.
            float ClothHash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
            }

            // Lattice coordinates wrap in X, so the field is seamless where U wraps from 1 back
            // to 0 -- without this a garment modelled as a closed cylinder shows a hard seam
            // line down the wrap.
            float ClothValueNoise(float2 p, float tileX)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float ix0 = fmod(i.x + tileX, tileX);
                float ix1 = fmod(i.x + 1.0 + tileX, tileX);

                float a = ClothHash(float2(ix0, i.y));
                float b = ClothHash(float2(ix1, i.y));
                float c = ClothHash(float2(ix0, i.y + 1.0));
                float d = ClothHash(float2(ix1, i.y + 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float ClothWrinkleField(float2 uv)
            {
                // Stretched along V so creases run with the drape rather than looking like static.
                // Octaves step by whole multiples to keep every level tiling in U.
                float tile = max(1.0, floor(_WrinkleScale + 0.5));
                float2 p = float2(uv.x * tile, uv.y * (tile / max(_WrinkleStretch, 1.0)));
                float n = ClothValueNoise(p, tile) * 0.6;
                n += ClothValueNoise(p * 2.0, tile * 2.0) * 0.3;
                n += ClothValueNoise(p * 4.0, tile * 4.0) * 0.1;
                return n;
            }

            // ---- BRDF terms ---------------------------------------------------------------
            // Charlie distribution + Ashikhmin visibility: the standard cloth sheen lobe, which
            // is what gives fabric its bright grazing-angle bloom instead of a hard hot spot.
            float Cloth_D_Charlie(float roughness, float NoH)
            {
                float invR = 1.0 / max(roughness, 1e-3);
                float cos2h = NoH * NoH;
                float sin2h = max(1.0 - cos2h, 0.0078125);
                return (2.0 + invR) * pow(sin2h, invR * 0.5) / (2.0 * PI);
            }

            float Cloth_V_Ashikhmin(float NoV, float NoL)
            {
                return saturate(1.0 / (4.0 * (NoL + NoV - NoL * NoV) + 1e-4));
            }

            // Anisotropic GGX: stretches the highlight along the fold direction, which is the
            // core of the satin/silk look in the reference.
            float Cloth_D_GGX_Aniso(float at, float ab, float ToH, float BoH, float NoH)
            {
                float3 v = float3(ab * ToH, at * BoH, at * ab * NoH);
                float v2 = dot(v, v);
                float a2 = at * ab;
                float w2 = a2 / max(v2, 1e-6);
                return a2 * w2 * w2 * (1.0 / PI);
            }

            float Cloth_V_SmithGGX_Aniso(float at, float ab, float ToV, float BoV, float ToL, float BoL, float NoV, float NoL)
            {
                float lambdaV = NoL * length(float3(at * ToV, ab * BoV, NoV));
                float lambdaL = NoV * length(float3(at * ToL, ab * BoL, NoL));
                return 0.5 / max(lambdaV + lambdaL, 1e-5);
            }

            float3 Cloth_F_Schlick(float3 f0, float VoH)
            {
                float f = pow(1.0 - VoH, 5.0);
                return f0 + (1.0 - f0) * f;
            }

            struct ClothSurface
            {
                float3 N, T, B, V;
                float  at, ab, sheenRough;
                float  NoV;
            };

            float3 ShadeClothLight(ClothSurface s, float3 L, float3 lightColor, float atten)
            {
                float3 H = SafeNormalize(L + s.V);
                float NoL = dot(s.N, L);
                float NoH = saturate(dot(s.N, H));
                float VoH = saturate(dot(s.V, H));

                // Wrapped diffuse keeps the shadow terminator soft, the way thick fabric reads.
                float wrapped = saturate((NoL + _DiffuseWrap) / (1.0 + _DiffuseWrap));
                float3 diffuse = _BaseColor.rgb * wrapped;

                float NoLsat = saturate(NoL);

                float sheen = Cloth_D_Charlie(s.sheenRough, NoH) * Cloth_V_Ashikhmin(s.NoV, NoLsat);
                float3 sheenTerm = _SheenColor.rgb * sheen * _SheenIntensity * NoLsat;

                float ToH = dot(s.T, H);
                float BoH = dot(s.B, H);
                float ToV = dot(s.T, s.V);
                float BoV = dot(s.B, s.V);
                float ToL = dot(s.T, L);
                float BoL = dot(s.B, L);

                float Da = Cloth_D_GGX_Aniso(s.at, s.ab, ToH, BoH, NoH);
                float Va = Cloth_V_SmithGGX_Aniso(s.at, s.ab, ToV, BoV, ToL, BoL, s.NoV, NoLsat);
                float3 Fa = Cloth_F_Schlick(float3(0.045, 0.045, 0.045), VoH);
                float3 specTerm = _SpecColor2.rgb * (Da * Va) * Fa * _SpecIntensity * NoLsat;

                return (diffuse + sheenTerm + specTerm) * lightColor * atten;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nrmInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS = nrmInputs.normalWS;
                OUT.tangentWS = nrmInputs.tangentWS;
                OUT.bitangentWS = nrmInputs.bitangentWS;
                OUT.uv = IN.uv;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.fogCoord = ComputeFogFactor(posInputs.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 T = normalize(IN.tangentWS);
                float3 B = normalize(IN.bitangentWS);

                // A closed garment (a tube) has no continuous UV wrap: the last column of quads
                // sweeps U backwards across the whole range, smearing the noise into a seam.
                // Sampling cylindrically from object space sidesteps UVs entirely, and the
                // tiling noise makes the angular wrap continuous.
                float2 wuv = IN.uv;
                if (_WrinkleCylindrical > 0.5)
                {
                    float angle = atan2(IN.positionOS.z, IN.positionOS.x) * (1.0 / (2.0 * PI)) + 0.5;
                    wuv = float2(angle, IN.positionOS.y);
                }

                float wrinkle = ClothWrinkleField(wuv);

                // Perturb the normal by the gradient of the wrinkle field.
                if (_WrinkleStrength > 0.001)
                {
                    float e = 0.004;
                    float wu = ClothWrinkleField(wuv + float2(e, 0));
                    float wv = ClothWrinkleField(wuv + float2(0, e));
                    float2 grad = float2(wu - wrinkle, wv - wrinkle) / e;
                    N = normalize(N - (T * grad.x + B * grad.y) * _WrinkleStrength * 0.02);
                    T = normalize(T - N * dot(N, T));
                    B = normalize(cross(N, T));
                }

                float3 V = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                ClothSurface s;
                s.N = N; s.T = T; s.B = B; s.V = V;
                s.NoV = saturate(dot(N, V)) + 1e-4;
                s.sheenRough = _SheenRoughness;

                float roughness = max(1.0 - _Smoothness, 0.02);
                roughness *= roughness;

                // Vary roughness with the weave so the highlight breaks into scattered soft
                // glints instead of one continuous mirror streak down each fold.
                roughness *= 1.0 + (wrinkle - 0.5) * _RoughnessBreakup * 1.6;
                roughness = clamp(roughness, 0.002, 1.0);
                float agg = sqrt(1.0 - saturate(abs(_Anisotropy)) * 0.9);
                if (_Anisotropy >= 0)
                {
                    s.at = max(roughness / agg, 1e-3);
                    s.ab = max(roughness * agg, 1e-3);
                }
                else
                {
                    s.at = max(roughness * agg, 1e-3);
                    s.ab = max(roughness / agg, 1e-3);
                }

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float3 color = ShadeClothLight(s, mainLight.direction, mainLight.color,
                                               mainLight.distanceAttenuation * mainLight.shadowAttenuation);

                #ifdef _ADDITIONAL_LIGHTS
                uint lightCount = GetAdditionalLightsCount();
                for (uint li = 0u; li < lightCount; ++li)
                {
                    Light addLight = GetAdditionalLight(li, IN.positionWS, half4(1, 1, 1, 1));
                    color += ShadeClothLight(s, addLight.direction, addLight.color,
                                             addLight.distanceAttenuation * addLight.shadowAttenuation);
                }
                #endif

                // Ambient: fabric picks up a lot of sky bounce at grazing angles.
                float3 ambient = SampleSH(N) * _AmbientIntensity;
                color += _BaseColor.rgb * ambient;
                color += _SheenColor.rgb * ambient * (1.0 - s.NoV) * 0.35 * _SheenIntensity;

                float fresnel = pow(1.0 - s.NoV, _FresnelPower);
                color += _FresnelColor.rgb * fresnel * _FresnelIntensity;

                color = MixFog(color, IN.fogCoord);
                return half4(color, 1.0);
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
            // Cast shadows from back faces: on thin, closed garments front-face casting puts
            // the depth surface flush against the lit side and stipples it with shadow acne.
            Cull Front

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _DiffuseWrap;
                float4 _SheenColor;
                float _SheenRoughness;
                float _SheenIntensity;
                float _Smoothness;
                float _Anisotropy;
                float4 _SpecColor2;
                float _SpecIntensity;
                float _RoughnessBreakup;
                float4 _FresnelColor;
                float _FresnelPower;
                float _FresnelIntensity;
                float _WrinkleStrength;
                float _WrinkleScale;
                float _WrinkleStretch;
                float _WrinkleCylindrical;
                float _AmbientIntensity;
            CBUFFER_END

            float3 _LightDirection;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct ShadowVaryings
            {
                float4 positionHCS : SV_POSITION;
            };

            ShadowVaryings ShadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                positionWS = ApplyShadowBias(positionWS, normalWS, _LightDirection);
                OUT.positionHCS = TransformWorldToHClip(positionWS);
                return OUT;
            }

            half4 ShadowFrag(ShadowVaryings IN) : SV_Target
            {
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

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _DiffuseWrap;
                float4 _SheenColor;
                float _SheenRoughness;
                float _SheenIntensity;
                float _Smoothness;
                float _Anisotropy;
                float4 _SpecColor2;
                float _SpecIntensity;
                float _RoughnessBreakup;
                float4 _FresnelColor;
                float _FresnelPower;
                float _FresnelIntensity;
                float _WrinkleStrength;
                float _WrinkleScale;
                float _WrinkleStretch;
                float _WrinkleCylindrical;
                float _AmbientIntensity;
            CBUFFER_END

            struct DepthAttributes { float4 positionOS : POSITION; };
            struct DepthVaryings { float4 positionHCS : SV_POSITION; };

            DepthVaryings DepthVert(DepthAttributes IN)
            {
                DepthVaryings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 DepthFrag(DepthVaryings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    Fallback Off
}
