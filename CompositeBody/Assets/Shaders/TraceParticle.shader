Shader "CompositeBody/TraceParticle"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Range(0, 8)) = 1.6
        _Falloff ("Falloff", Range(0.5, 8)) = 2.4

        [Toggle] _Streak ("Streak Shape", Float) = 0
        _StreakTaper ("Streak End Taper", Range(0.2, 8)) = 1.6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Plane"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ParticleUnlit"
            Tags { "LightMode" = "UniversalForward" }

            // Additive. Motes only ever read against the dark, and additive is what lets a
            // hundred faint overlapping points accumulate into the bright core of the cloud
            // instead of the nearest one simply covering the rest.
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _Falloff;
                float _Streak;
                float _StreakTaper;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;      // particle colour-over-lifetime
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float4 color       : COLOR;
                float  fogCoord    : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = posInputs.positionCS;
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                OUT.fogCoord = ComputeFogFactor(posInputs.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 d = IN.uv - 0.5;

                float shape;
                if (_Streak > 0.5)
                {
                    // Stretched billboards are extended along U, so the line stays thin across
                    // V and tapers away at both ends in U. A radial falloff here would give a
                    // fat lozenge instead of a hairline.
                    float across = saturate(1.0 - abs(d.y) * 2.0);
                    float along = saturate(1.0 - abs(d.x) * 2.0);
                    shape = pow(across, _Falloff) * pow(along, _StreakTaper);
                }
                else
                {
                    // Procedural soft point: no texture to author, sample or mip, and it stays
                    // round at any size.
                    shape = pow(saturate(1.0 - length(d) * 2.0), _Falloff);
                }

                float3 rgb = _Color.rgb * IN.color.rgb * _Intensity;
                float alpha = shape * _Color.a * IN.color.a;

                rgb = MixFog(rgb, IN.fogCoord);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
