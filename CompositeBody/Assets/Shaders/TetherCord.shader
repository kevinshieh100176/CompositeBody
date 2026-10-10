Shader "CompositeBody/TetherCord"
{
    Properties
    {
        [Header(Cord)]
        _CordColor ("Slack Colour", Color) = (0.42, 0.03, 0.04, 1)
        _HotColor ("Taut Colour", Color) = (1.0, 0.20, 0.14, 1)
        _Intensity ("Intensity", Range(0, 6)) = 1.3

        [Header(Shape)]
        _CoreWidth ("Core Width", Range(0.05, 1)) = 0.45
        _EdgeFalloff ("Edge Falloff", Range(0.5, 6)) = 1.8

        [Header(Tension)]
        // Driven per-frame by PlayerTether through a MaterialPropertyBlock.
        _Tension ("Tension", Range(0, 1)) = 0
        _TensionGlow ("Tension Glow", Range(0, 4)) = 1.7
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
            Name "Cord"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            // A LineRenderer is a two-sided strip; culling either face makes it vanish at some
            // camera angles.
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CompositeFog.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CordColor;
                float4 _HotColor;
                float _Intensity;
                float _CoreWidth;
                float _EdgeFalloff;
                float _Tension;
                float _TensionGlow;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float4 color       : COLOR;
                float  fogCoord    : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;   // height fog needs to know where this is
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = posInputs.positionCS;
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                OUT.positionWS = posInputs.positionWS;
                OUT.fogCoord = ComputeFogFactor(posInputs.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                // V runs across the ribbon. Shading it off toward the edges is what rounds a
                // flat camera-facing strip into something that reads as cord rather than tape.
                float across = abs(IN.uv.y - 0.5) * 2.0;

                float body = pow(saturate(1.0 - across), _EdgeFalloff);
                float core = pow(saturate(1.0 - across / max(_CoreWidth, 1e-3)), 2.0);

                // Pulled taut the cord lights up along its centre line, which is the cue that
                // sells it as elastic under load rather than a rope that simply got shorter.
                float3 col = lerp(_CordColor.rgb, _HotColor.rgb, _Tension);
                col += _HotColor.rgb * core * _TensionGlow * _Tension;
                col *= _Intensity * IN.color.rgb;

                float alpha = saturate(body * _CordColor.a * IN.color.a);

                col = CompositeFogMix(col, IN.positionWS, IN.fogCoord);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
