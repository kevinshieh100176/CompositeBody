Shader "CompositeBody/FrostedGlass"
{
    Properties
    {
        [Header(Glass)]
        _TintColor ("Tint", Color) = (0.85, 0.93, 1.0, 1)
        _TintStrength ("Tint Strength", Range(0, 1)) = 0.25
        _Opacity ("Opacity (milkiness)", Range(0, 1)) = 0.15
        // How much of the sharp scene behind the glass is replaced by the frosted layer. Water and
        // other transparents are missing from the blurred backdrop, so below 1 they show through.
        _Coverage ("Frost Coverage", Range(0, 1)) = 0.5

        [Header(Frost)]
        _BlurRadius ("Blur Radius", Range(0, 0.05)) = 0.012
        _FrostNoiseScale ("Frost Grain Scale", Range(10, 2000)) = 500
        _FrostDistortion ("Frost Distortion", Range(0, 0.02)) = 0.004

        [Header(Surface)]
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3.0
        _FresnelBrightness ("Fresnel Brightness", Range(0, 2)) = 0.35
        _Smoothness ("Highlight Smoothness", Range(0, 1)) = 0.6
        _SpecIntensity ("Highlight Intensity", Range(0, 2)) = 0.4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "FrostedGlass"
            Tags { "LightMode" = "UniversalForward" }

            // The pass writes the already-blurred backdrop itself, so it replaces what is behind
            // it rather than blending. Alpha only exists to let the milky layer sit on top.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _TintColor;
                float _TintStrength;
                float _Opacity;
                float _Coverage;
                float _BlurRadius;
                float _FrostNoiseScale;
                float _FrostDistortion;
                float _FresnelPower;
                float _FresnelBrightness;
                float _Smoothness;
                float _SpecIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float  fogCoord    : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);

                OUT.positionHCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = nrm.normalWS;
                OUT.fogCoord = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float2 uv = GetNormalizedScreenSpaceUV(IN.positionHCS);

                // Per-pixel grain, in world space so it stays stuck to the glass instead of
                // swimming with the camera -- in VR the swim would read immediately.
                float2 grainCoord = IN.positionWS.xy + IN.positionWS.zz * 0.7;
                float2 grain = float2(Hash21(grainCoord * _FrostNoiseScale),
                                      Hash21(grainCoord * _FrostNoiseScale + 17.3)) * 2.0 - 1.0;

                // Golden-angle spiral blur, rotated per pixel so 16 taps don't band.
                const int TAPS = 16;
                const float GOLDEN = 2.39996323;
                float rot = Hash21(grainCoord * _FrostNoiseScale + 3.7) * 6.2831853;
                float aspect = _ScreenParams.x / _ScreenParams.y;

                half3 acc = 0;
                [unroll]
                for (int i = 0; i < TAPS; i++)
                {
                    float r = sqrt((i + 0.5) / TAPS) * _BlurRadius;
                    float a = i * GOLDEN + rot;
                    float2 off = float2(cos(a), sin(a)) * r;
                    off.x /= aspect;
                    acc += SampleSceneColor(uv + off + grain * _FrostDistortion);
                }
                half3 backdrop = acc / TAPS;

                half3 tint = lerp(backdrop, backdrop * _TintColor.rgb, _TintStrength);

                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float fres = pow(1.0 - saturate(dot(N, V)), _FresnelPower);

                Light mainLight = GetMainLight();
                float3 H = normalize(mainLight.direction + V);
                float specPower = exp2(10.0 * _Smoothness + 1.0);
                half3 spec = pow(saturate(dot(N, H)), specPower) * mainLight.color * _SpecIntensity;

                half3 milk = _TintColor.rgb * (0.5 + 0.5 * mainLight.color);
                half3 col = tint + fres * _FresnelBrightness * _TintColor.rgb + spec;

                // Alpha decides how much of the *sharp* scene is replaced by the blurred backdrop.
                half alpha = saturate(_Coverage + _Opacity * 0.5);
                col = lerp(col, milk, _Opacity * 0.5);

                col = MixFog(col, IN.fogCoord);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
