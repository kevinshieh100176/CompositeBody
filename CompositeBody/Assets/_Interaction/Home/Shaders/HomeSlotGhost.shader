Shader "CompositeBody/HomeSlotGhost"
{
    // Where a carried Home fragment belongs: a blue paper outline in its slot of the mini-house.
    // A variant of CompositeBody/GhostHalf (blue fill + rim, transparent, ZWrite Off) for FLAT
    // geometry: a sheet of paper faces the camera with one facing ratio across the whole of it, so
    // GhostHalf's fresnel draws nothing useful. The outline comes from the mesh instead -- the
    // builder bakes a thin ribbon along the paper's boundary edges into the ghost mesh and marks it
    // in vertex colour red (1 on the outer edge, 0 on the inner edge of the ribbon, 0 on the paper).
    //
    // Cull defaults to Off: a sheet is seen from both sides. _Alpha (overall strength) and _Boost
    // (0..1 proximity of the carried fragment) are driven per renderer by HomeSlotGhost through a
    // MaterialPropertyBlock; every property is in UnityPerMaterial, so the material stays SRP-Batcher
    // compatible. Single-pass instanced stereo.
    Properties
    {
        [Header(Ghost)]
        _GhostColor ("Ghost Colour", Color) = (0.42, 0.72, 1.0, 1)
        _FillAlpha ("Fill Alpha", Range(0, 1)) = 0.10

        [Header(Outline)]
        _EdgeColor ("Edge Colour", Color) = (0.70, 0.88, 1.0, 1)
        _EdgeAlpha ("Edge Alpha", Range(0, 1)) = 0.95
        _EdgeSharpness ("Edge Sharpness (falloff across the ribbon)", Range(0.5, 8)) = 1.5
        _RimPower ("Rim Power (grazing views)", Range(0.5, 12)) = 4.0
        _RimIntensity ("Rim Brightness", Range(0, 6)) = 1.0

        [Header(Driven)]
        _Alpha ("Alpha (driven)", Range(0, 1)) = 1
        _Boost ("Proximity Boost (driven)", Range(0, 1)) = 0
        _BoostBrightness ("Boost Brightness", Range(0, 3)) = 0.6
        _BoostFill ("Boost Fill Alpha", Range(0, 1)) = 0.12

        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
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
            Name "Ghost"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/CompositeFog.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _GhostColor;
                float _FillAlpha;
                float4 _EdgeColor;
                float _EdgeAlpha;
                float _EdgeSharpness;
                float _RimPower;
                float _RimIntensity;
                float _Alpha;
                float _Boost;
                float _BoostBrightness;
                float _BoostFill;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 viewDirWS   : TEXCOORD1;
                float  fogCoord    : TEXCOORD2;
                float3 positionWS  : TEXCOORD3;
                float  edge        : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nrmInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionHCS = posInputs.positionCS;
                OUT.normalWS = nrmInputs.normalWS;
                OUT.viewDirWS = GetWorldSpaceViewDir(posInputs.positionWS);
                OUT.positionWS = posInputs.positionWS;
                OUT.fogCoord = ComputeFogFactor(posInputs.positionCS.z);
                OUT.edge = IN.color.r;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(IN.viewDirWS);

                // abs(): with Cull Off the back of the sheet is drawn with the front's normal.
                float rim = pow(saturate(1.0 - abs(dot(N, V))), _RimPower);

                // The baked ribbon: 1 at the paper's edge, falling to 0 a few millimetres in.
                float edge = pow(saturate(IN.edge), _EdgeSharpness);

                // Proximity brightens the OUTLINE (towards white) rather than the whole sheet, so the
                // shape stays legible as it strengthens; the fill only gains a little alpha.
                float boost = saturate(_Boost);
                float3 fill = _GhostColor.rgb * (1.0 + rim * _RimIntensity);
                float3 outline = _EdgeColor.rgb * (1.0 + boost * _BoostBrightness);
                float3 col = lerp(fill, outline, edge);

                float alpha = saturate(_FillAlpha + boost * _BoostFill + edge * _EdgeAlpha + rim * 0.25);
                alpha *= saturate(_Alpha) * _GhostColor.a;

                col = CompositeFogMix(col, IN.positionWS, IN.fogCoord);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
