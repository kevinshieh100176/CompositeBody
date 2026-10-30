Shader "CompositeBody/VolumetricTube"
{
    // A trail that reads as a cylinder of lit air rather than as a flat strip.
    //
    // THE GEOMETRY IS STILL A RIBBON. A TrailRenderer makes a strip of quads, and under
    // LineAlignment.View that strip always turns to face the eye -- which is exactly why it read
    // as a decal: something that always faces you has no thickness to see, and in stereo both
    // eyes agree it is flat.
    //
    // Rather than generate a tube mesh per frame, this shades the strip AS a tube. Across its
    // width v runs 0..1; a cylinder seen side-on is thickest at the middle and thins to nothing
    // at the silhouette, and the chord length through it is sqrt(1 - (2v-1)^2). That one line is
    // the whole illusion, and it is correct rather than approximate: it is the actual path length
    // through a cylinder of that radius.
    //
    // Paired with a soft depth intersection so the trail does not cut a hard line where it
    // crosses the floor or a 真人 -- the same reasoning, and the same depth texture, as the cone
    // and the point glow.
    //
    // Additive and never written to depth: this is light in the air, and air does not occlude.

    Properties
    {
        [HDR] _Color ("Colour", Color) = (0.62, 0.22, 1.0, 1)
        _Intensity ("Intensity", Range(0, 8)) = 1.4
        _Thickness ("Thickness Gamma", Range(0.25, 4)) = 1.0
        _CoreBoost ("Core Brightness", Range(1, 6)) = 2.2
        _Rolloff ("Highlight Rolloff", Range(0.05, 4)) = 1.2
        _DepthSoftness ("Intersection Softness (m)", Range(0.01, 2)) = 0.3
        _Reveal ("Reveal", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+110"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Plane"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "VolumetricTube"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "CompositeFog.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _Thickness;
                float _CoreBoost;
                float _Rolloff;
                float _DepthSoftness;
                float _Reveal;
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
                float4 color       : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
                float4 screenPos   : TEXCOORD3;
                float  fogCoord    : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.screenPos = ComputeScreenPos(posInputs.positionCS);
                OUT.uv = IN.uv;

                // The TrailRenderer's own gradient arrives as vertex colour, so the fade along
                // the trail's length stays authored in the component where it can be seen.
                OUT.color = IN.color;

                OUT.fogCoord = ComputeFogFactor(posInputs.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                if (_Reveal <= 0.002) discard;

                // Chord through a cylinder of radius 1 at offset (2v - 1) from its axis. Zero at
                // the silhouette, one at the core -- the thickness of air the ray passes through.
                float offset = IN.uv.y * 2.0 - 1.0;
                float chord = sqrt(saturate(1.0 - offset * offset));
                if (chord <= 1e-4) discard;

                float thickness = pow(chord, max(_Thickness, 0.25));

                // The core is brighter than the chord alone would make it: a real shaft is
                // densest where it is deepest, and this keeps the centreline reading as a line
                // rather than as a wide smear.
                float core = 1.0 + (_CoreBoost - 1.0) * chord * chord;

                // Soft where it meets geometry, for the same reason the cone is.
                float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 1e-6);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float fragmentEye = LinearEyeDepth(IN.positionHCS.z, _ZBufferParams);
                float fade = saturate((sceneEye - fragmentEye) / max(_DepthSoftness, 1e-4));

                float strength = thickness * core * IN.color.a * _Reveal * fade;
                strength = strength / (1.0 + strength * _Rolloff);

                float3 rgb = _Color.rgb * IN.color.rgb * _Intensity * strength;
                rgb = CompositeFogAttenuate(rgb, IN.positionWS, IN.fogCoord);

                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
