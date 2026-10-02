Shader "CompositeBody/SeaSurface"
{
    Properties
    {
        [Header(Water)]
        _DeepColor ("Deep Colour", Color) = (0.016, 0.028, 0.042, 1)
        _SheenColor ("Grazing Sheen", Color) = (0.30, 0.42, 0.52, 1)
        _Sheen ("Sheen Strength", Range(0, 2)) = 0.85

        [Header(Waves)]
        _WaveAmp ("Amplitude", Range(0, 0.4)) = 0.055
        _WaveScale ("Scale", Range(0.05, 3)) = 0.42
        _WaveSpeed ("Speed", Range(0, 3)) = 0.45

        [Header(Grazing)]
        // The sea is almost black looked straight down at, and picks up light only
        // towards the horizon -- which is what makes the floor read as water rather
        // than as a dark plane.
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3.4

        [Header(Highlight)]
        _SpecColor2 ("Highlight Colour", Color) = (0.55, 0.66, 0.78, 1)
        _SpecSharpness ("Highlight Sharpness", Range(4, 256)) = 48
        _SpecStrength ("Highlight Strength", Range(0, 2)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Sea"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor;
                float4 _SheenColor;
                float _Sheen;
                float _WaveAmp;
                float _WaveScale;
                float _WaveSpeed;
                float _FresnelPower;
                float4 _SpecColor2;
                float _SpecSharpness;
                float _SpecStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float  fogCoord    : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Three crossing swells. Deliberately analytic rather than textured: the
            // normal below is a finite difference of this same function, so the
            // lighting always agrees with the displacement.
            float WaveHeight(float2 p, float t)
            {
                float h = 0.0;
                h += sin(p.x * 0.70 + t * 0.90) * 0.55;
                h += sin(p.y * 0.90 - t * 0.70) * 0.42;
                h += sin((p.x + p.y) * 1.70 + t * 1.50) * 0.18;
                return h;
            }

            float SampleHeight(float2 worldXZ, float t)
            {
                return WaveHeight(worldXZ * _WaveScale, t) * _WaveAmp;
            }

            Varyings vert (Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float t = _Time.y * _WaveSpeed;

                positionWS.y += SampleHeight(positionWS.xz, t);

                // Central differences, in the same world units the displacement used.
                const float eps = 0.35;
                float hL = SampleHeight(positionWS.xz + float2(-eps, 0), t);
                float hR = SampleHeight(positionWS.xz + float2( eps, 0), t);
                float hD = SampleHeight(positionWS.xz + float2(0, -eps), t);
                float hU = SampleHeight(positionWS.xz + float2(0,  eps), t);

                float3 tangentX = float3(2.0 * eps, hR - hL, 0.0);
                float3 tangentZ = float3(0.0, hU - hD, 2.0 * eps);
                output.normalWS = normalize(cross(tangentZ, tangentX));

                output.positionWS = positionWS;
                output.positionHCS = TransformWorldToHClip(positionWS);
                output.fogCoord = ComputeFogFactor(output.positionHCS.z);
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 N = normalize(input.normalWS);
                float3 V = normalize(GetCameraPositionWS() - input.positionWS);

                float fresnel = pow(saturate(1.0 - saturate(dot(N, V))), _FresnelPower);
                float3 col = lerp(_DeepColor.rgb, _SheenColor.rgb, saturate(fresnel * _Sheen));

                // Fixed light direction rather than the scene's main light: this surface is
                // lit by an implied sky the void has no actual lamp for, and a fixed vector
                // keeps the highlight identical on both headsets regardless of scene setup.
                float3 L = normalize(float3(0.35, 0.62, -0.70));
                float3 H = normalize(L + V);
                float spec = pow(saturate(dot(N, H)), _SpecSharpness) * _SpecStrength;
                col += _SpecColor2.rgb * spec;

                col = MixFog(col, input.fogCoord);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
