Shader "CompositeBody/GhostHalf"
{
    Properties
    {
        [Header(Ghost)]
        _GhostColor ("Ghost Colour", Color) = (0.42, 0.72, 1.0, 1)
        _FillAlpha ("Fill Alpha", Range(0, 1)) = 0.06

        [Header(Outline)]
        _RimPower ("Rim Power", Range(0.5, 12)) = 4.0
        _RimIntensity ("Rim Brightness", Range(0, 6)) = 1.5
        _EdgeAlpha ("Edge Alpha", Range(0, 1)) = 0.9
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
            // A ghost that wrote depth would occlude the half the player is actually meant to
            // be looking at, and would sort against itself along the seam where the two halves
            // meet -- the one place the read has to stay clean.
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _GhostColor;
                float _FillAlpha;
                float _RimPower;
                float _RimIntensity;
                float _EdgeAlpha;
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
                float3 viewDirWS   : TEXCOORD1;
                float  fogCoord    : TEXCOORD2;
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
                OUT.fogCoord = ComputeFogFactor(posInputs.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(IN.viewDirWS);

                // Facing ratio: near 1 wherever the surface turns away from the camera, which is
                // precisely the silhouette. Taking the outline from the mesh this way keeps it a
                // single unlit pass -- an inverted-hull outline would double the draw calls per
                // ghosted object, and a screen-space one would mean a URP renderer feature and a
                // pipeline asset edit for what is only ever a hint that an object is not yours.
                //
                // The trade is that this follows the normals, so it draws a true outline only
                // where the surface curves. On hard-edged geometry each flat face has one facing
                // ratio across the whole of it, and the effect reads as faces near edge-on going
                // opaque rather than as a drawn line.
                float rim = pow(saturate(1.0 - saturate(dot(N, V))), _RimPower);

                float3 col = _GhostColor.rgb * (1.0 + rim * _RimIntensity);

                // Edge opacity is its own control rather than being driven off the brightness.
                // Sharing one number meant any rim strong enough to read as a line also drove
                // alpha to fully opaque, which turned the whole ghost solid -- the fill has to
                // stay near-invisible for the edge to be the thing you see.
                float alpha = saturate(_FillAlpha + rim * _EdgeAlpha) * _GhostColor.a;

                col = MixFog(col, IN.fogCoord);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
