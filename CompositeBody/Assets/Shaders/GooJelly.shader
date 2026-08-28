Shader "CompositeBody/GooJelly"
{
    Properties
    {
        [Header(Jelly Colour)]
        _ShallowColor ("Shallow Colour", Color) = (0.45, 0.85, 0.55, 1)
        _DeepColor ("Deep Colour", Color) = (0.03, 0.18, 0.10, 1)
        _BaseAlpha ("Base Alpha", Range(0, 1)) = 0.42
        _EdgeOpacity ("Edge Opacity", Range(0, 2)) = 0.85

        [Header(Wet Surface)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.94
        _SpecIntensity ("Specular Intensity", Range(0, 4)) = 1.1
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3.0
        _FresnelIntensity ("Fresnel Intensity", Range(0, 3)) = 0.7

        [Header(Translucency)]
        _TranslucencyPower ("Translucency Power", Range(1, 16)) = 4.0
        _TranslucencyIntensity ("Translucency Intensity", Range(0, 4)) = 1.3
        _AmbientIntensity ("Ambient Intensity", Range(0, 2)) = 0.75

        [Header(Goo Motion)]
        _NoiseScale ("Blob Scale", Float) = 5.5
        _FlowSpeed ("Flow Speed", Float) = 0.35
        _WobbleAmount ("Wobble Amount", Range(0, 2)) = 1.15
        _BaseFill ("Base Fill", Range(0, 1)) = 0.62
        _MinGapFrac ("Minimum Gap", Range(0, 0.5)) = 0.04

        [Header(Surface Ripples)]
        _RippleScale ("Ripple Scale", Float) = 26
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.35
        _RippleSpeed ("Ripple Speed", Float) = 0.9
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // Premultiplied alpha: lets the wet highlight and rim stay at full strength on a
            // see-through surface. With straight SrcAlpha blending the specular is scaled down
            // by alpha, which is exactly the cue that makes it read as jelly rather than paint.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float _BaseAlpha;
                float _EdgeOpacity;
                float _Smoothness;
                float _SpecIntensity;
                float _FresnelPower;
                float _FresnelIntensity;
                float _TranslucencyPower;
                float _TranslucencyIntensity;
                float _AmbientIntensity;
                float _NoiseScale;
                float _FlowSpeed;
                float _WobbleAmount;
                float _BaseFill;
                float _MinGapFrac;
                float _RippleScale;
                float _RippleStrength;
                float _RippleSpeed;
            CBUFFER_END

            // Touch deformers (hands, elbows, props). xyz = world centre, w = radius.
            // Declared outside UnityPerMaterial: arrays there break SRP batcher compatibility,
            // and this shader runs on a single avatar so batching is not worth the constraint.
            #define GOO_MAX_INFLUENCERS 8
            float4 _GooInfluencers[GOO_MAX_INFLUENCERS];
            int _GooInfluencerCount;

            // ---- noise -------------------------------------------------------------------
            float GooHash(float3 p)
            {
                return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453123);
            }

            float GooValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = GooHash(i + float3(0, 0, 0));
                float n100 = GooHash(i + float3(1, 0, 0));
                float n010 = GooHash(i + float3(0, 1, 0));
                float n110 = GooHash(i + float3(1, 1, 0));
                float n001 = GooHash(i + float3(0, 0, 1));
                float n101 = GooHash(i + float3(1, 0, 1));
                float n011 = GooHash(i + float3(0, 1, 1));
                float n111 = GooHash(i + float3(1, 1, 1));

                float x00 = lerp(n000, n100, f.x);
                float x10 = lerp(n010, n110, f.x);
                float x01 = lerp(n001, n101, f.x);
                float x11 = lerp(n011, n111, f.x);
                return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
            }

            float GooFbm(float3 p)
            {
                float n = GooValueNoise(p) * 0.6;
                n += GooValueNoise(p * 2.03) * 0.28;
                n += GooValueNoise(p * 4.11) * 0.12;
                return n;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;   // skinned shell surface
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 shellData  : TEXCOORD1;  // x = available goo thickness at this vertex
                float3 restPos    : TEXCOORD2;  // bind-pose body position, anchors the noise
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                float3 restPos     : TEXCOORD3;
                float  fillRatio   : TEXCOORD4; // 0 = squashed onto body, 1 = fully swollen
                float  fogCoord    : TEXCOORD5;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;

                float thickness = max(IN.shellData.x, 1e-5);
                float3 n = normalize(IN.normalOS);

                // The authored shell sits one full thickness off the body, so stepping back
                // along the normal recovers the body surface the goo is resting on.
                float3 bodyOS = IN.positionOS.xyz - n * thickness;

                // Noise is sampled in bind-pose space so the blobs stay anchored to the body
                // and crawl over it, rather than the body swimming through a static field.
                float3 samplePos = IN.restPos * _NoiseScale + float3(0.0, -_Time.y * _FlowSpeed, _Time.y * _FlowSpeed * 0.6);
                float blob = GooFbm(samplePos);

                float fill = _BaseFill + (blob - 0.5) * _WobbleAmount;

                // Clamping to the gap is what makes contact read: the goo can never pass
                // through the body, so wherever the wobble bottoms out it visibly flattens
                // against the surface underneath.
                fill = clamp(fill, _MinGapFrac, 1.0);

                float3 displacedOS = bodyOS + n * (thickness * fill);
                float3 positionWS = TransformObjectToWorld(displacedOS);
                float3 normalWS = TransformObjectToWorldNormal(n);

                // Touch deformation: push the surface out of any influencer sphere and bend the
                // normal to match, so a hand pressing in leaves a shaped dent, not a flat one.
                [loop]
                for (int i = 0; i < _GooInfluencerCount && i < GOO_MAX_INFLUENCERS; ++i)
                {
                    float3 c = _GooInfluencers[i].xyz;
                    float r = _GooInfluencers[i].w;
                    float3 delta = positionWS - c;
                    float d = length(delta);
                    if (d < r && d > 1e-5)
                    {
                        float3 dir = delta / d;
                        positionWS = c + dir * r;
                        normalWS = normalize(lerp(normalWS, dir, saturate(1.0 - d / r)));
                    }
                }

                OUT.positionWS = positionWS;
                OUT.normalWS = normalWS;
                OUT.positionHCS = TransformWorldToHClip(positionWS);
                OUT.uv = IN.uv;
                OUT.restPos = IN.restPos;
                OUT.fillRatio = saturate(fill);
                OUT.fogCoord = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                // Fine travelling ripples on top of the big blobs, so the surface reads as wet
                // and restless even where the silhouette is calm.
                if (_RippleStrength > 0.001)
                {
                    float e = 0.02;
                    float3 rp = IN.restPos * _RippleScale + float3(_Time.y * _RippleSpeed, _Time.y * _RippleSpeed * 0.7, 0);
                    float r0 = GooValueNoise(rp);
                    float rx = GooValueNoise(rp + float3(e, 0, 0));
                    float ry = GooValueNoise(rp + float3(0, e, 0));
                    float rz = GooValueNoise(rp + float3(0, 0, e));
                    float3 grad = float3(rx - r0, ry - r0, rz - r0) / e;
                    N = normalize(N - (grad - N * dot(N, grad)) * _RippleStrength * 0.06);
                }

                float NoV = saturate(dot(N, V)) + 1e-4;

                // Thin goo shows the body through it; thick goo absorbs toward the deep colour.
                float3 albedo = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(IN.fillRatio));

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float3 L = mainLight.direction;
                float atten = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                float NoL = saturate(dot(N, L));

                float3 diffuse = albedo * NoL;

                // Wet highlight.
                float3 H = SafeNormalize(L + V);
                float NoH = saturate(dot(N, H));
                float roughness = max(1.0 - _Smoothness, 0.02);
                roughness *= roughness;
                float a2 = roughness * roughness;
                float denom = (NoH * NoH * (a2 - 1.0) + 1.0);
                float D = a2 / max(PI * denom * denom, 1e-5);
                float3 spec = D * _SpecIntensity * NoL;

                // Back-scatter: light entering the far side and bleeding through the jelly.
                float trans = pow(saturate(dot(-L, V)), _TranslucencyPower);
                trans *= (1.0 - saturate(IN.fillRatio) * 0.65); // thin goo transmits most
                float3 translucency = _ShallowColor.rgb * trans * _TranslucencyIntensity;

                float3 ambient = SampleSH(N) * _AmbientIntensity * albedo;

                float fresnel = pow(1.0 - NoV, _FresnelPower);

                float alpha = saturate(_BaseAlpha + fresnel * _EdgeOpacity + IN.fillRatio * 0.18);

                // Body-coloured terms are scaled by how much goo the ray passed through;
                // reflected terms (highlight, rim) sit on top at full strength.
                float3 bodyTerms = diffuse * mainLight.color * atten
                                   + translucency * mainLight.color
                                   + ambient;

                float3 reflectedTerms = spec * mainLight.color * atten
                                        + _ShallowColor.rgb * fresnel * _FresnelIntensity;

                float3 color = bodyTerms * alpha + reflectedTerms;

                color = MixFog(color, IN.fogCoord);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
