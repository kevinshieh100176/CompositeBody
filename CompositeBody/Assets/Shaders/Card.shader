Shader "CompositeBody/Card"
{
    // A textured card drawn OVER the screen fade.
    //
    // WHY NOT URP/Unlit, which is what the O-0 opening cards use. Those cards live in a black
    // room and are additive, so they need no depth trickery: nothing is in front of them. The
    // end credits are the opposite case -- they sit on top of a full white ScreenFade quad that
    // is parented to the camera 8 cm from the eyes, drawn at Queue Overlay with ZTest Always.
    // Anything that depth-tests, or that sits in the same queue, loses to it. So this shader
    // makes the same two promises the fade makes -- ignore depth, draw late -- and then one
    // more: draw LATER than the fade (Overlay+100), so the queue, not the distance, decides.
    //
    // ALPHA BLEND, NOT ADDITIVE, and that is the whole reason this is not O0Title.mat with a
    // different texture. Additive adds light, which is the right answer for white type on
    // black and the wrong one for black type on white: adding anything to white is white, so
    // an additive credits card over the white-out is a perfectly invisible credits card.
    //
    // The card is placed metres away rather than centimetres (see CreditsCards.m_Distance).
    // A camera-parented quad at 8 cm is fine for the fade, which is one flat colour and needs
    // no stereo fusion at all; text at 8 cm asks the eyes to cross and is unreadable in a
    // headset. Depth here is a comfort setting, not an occlusion one -- ZTest Always means the
    // distance buys nothing but vergence.
    Properties
    {
        [MainTexture] _BaseMap ("Artwork", 2D) = "white" {}
        [MainColor]   _BaseColor ("Tint (alpha is the fade)", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Overlay+100"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Card"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);

                // The tint's alpha is the fade, multiplied into the artwork's own alpha rather
                // than replacing it, so antialiased glyph edges stay antialiased all the way
                // down. CardFade pushes this through a MaterialPropertyBlock.
                return half4(tex.rgb * _BaseColor.rgb, tex.a * _BaseColor.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
