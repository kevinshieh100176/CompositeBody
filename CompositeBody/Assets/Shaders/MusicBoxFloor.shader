Shader "CompositeBody/MusicBoxFloor"
{
    // 「觀眾與真人的地面似乎在旋轉，像是音樂盒」.
    //
    // NOTHING ACTUALLY ROTATES. The script says 似乎 -- SEEMS to rotate -- and that word is the
    // whole design. Turning the floor geometry, or the world, or the rig under a standing player
    // is the single most reliable way to make someone sick in VR: a large surface moving in the
    // lower field with the vestibular system reporting nothing is textbook vection. What turns
    // here is a PATTERN evaluated in polar coordinates on a stationary disc. The floor stays
    // exactly where the player's feet say it is, and only the marks on it move.
    //
    // Two mitigations beyond that, both deliberate. The pattern is concentric-plus-radial rather
    // than a spiral or a texture with a strong edge, because a rotating radial feature that fills
    // the periphery drives vection much harder than one bounded to a disc in front of you. And
    // the disc fades out with radius, so there is no rim travelling through peripheral vision --
    // a hard circular edge sweeping past is the part people actually feel.
    //
    // A music box reads as: a turning plate, a ring of pins, and the faint concentric scoring of
    // a cylinder. That is all this is -- rings that do not move, teeth that do.

    Properties
    {
        [HDR] _Color ("Colour", Color) = (0.55, 0.60, 0.78, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1

        [Header(Plate)]
        _Radius ("Radius (object space)", Range(0.05, 0.5)) = 0.5
        _EdgeFade ("Edge Fade", Range(0.01, 1)) = 0.55
        _InnerFade ("Inner Fade", Range(0, 0.5)) = 0.06

        [Header(Marks)]
        _Teeth ("Teeth", Range(3, 64)) = 18
        _ToothWidth ("Tooth Width", Range(0.01, 0.5)) = 0.1
        _ToothInner ("Tooth Inner Radius", Range(0, 1)) = 0.45
        _Rings ("Rings", Range(0, 40)) = 11
        _RingWeight ("Ring Strength", Range(0, 2)) = 0.35

        [Header(Turn)]
        _Phase ("Phase (seconds, driven)", Float) = 0
        _Speed ("Turns Per Minute", Range(0, 20)) = 3.2
        _Spin ("Spin (0 is still and dark)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+50"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "MusicBoxFloor"
            Tags { "LightMode" = "UniversalForward" }

            // Additive. The plate is light lying on the floor, not paint: it must not darken the
            // grey plane where the marks are absent, and it must not fight the beams for sort
            // order. Offset so it never z-fights the floor it sits a few millimetres above.
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CompositeFog.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _Radius;
                float _EdgeFade;
                float _InnerFade;
                float _Teeth;
                float _ToothWidth;
                float _ToothInner;
                float _Rings;
                float _RingWeight;
                float _Phase;
                float _Speed;
                float _Spin;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionOS  : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float  fogCoord    : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = posInputs.positionCS;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.positionWS = posInputs.positionWS;
                OUT.fogCoord = ComputeFogFactor(posInputs.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                if (_Spin <= 0.001) discard;

                // Polar, in the plate's own space. A Unity Quad is the XY plane, so the disc is
                // read off xy and the object is laid flat by its transform -- which keeps the
                // maths independent of how the plate happens to be rotated in the room.
                float2 p = IN.positionOS.xy;
                float r = length(p) / max(_Radius, 1e-4);
                if (r > 1.0) discard;

                float theta = atan2(p.y, p.x);

                // Radians per second from turns per minute. Driven phase rather than _Time, so
                // both headsets are on the same revolution -- two people watching one plate turn
                // at different angles is worse than no plate at all.
                float turn = _Phase * _Speed * (6.2831853 / 60.0);

                // The teeth: thin radial marks that travel. frac() of the turned angle gives a
                // saw per tooth; folding it to a distance from the mark and thresholding gives a
                // hard-edged pin without a texture.
                float toothPhase = frac((theta + turn) * _Teeth / 6.2831853);
                float toothDist = abs(toothPhase - 0.5) * 2.0;            // 0 between, 1 on a mark
                float teeth = smoothstep(1.0 - _ToothWidth, 1.0, toothDist);
                // Pins sit on the outer part of the plate, as they do on a cylinder.
                teeth *= smoothstep(_ToothInner, _ToothInner + 0.18, r);

                // The rings: concentric scoring that does NOT turn. Something stationary in the
                // pattern is what lets the eye see the rest of it turning -- a plate where
                // everything moves together just reads as a plate.
                float rings = abs(sin(r * _Rings * 3.14159)) ;
                rings = pow(rings, 8.0) * _RingWeight;

                // No rim and no hub. Both are hard edges, and a hard edge sweeping through
                // peripheral vision is the part of a rotating floor that people actually feel.
                float edge = 1.0 - smoothstep(1.0 - _EdgeFade, 1.0, r);
                float hub = smoothstep(0.0, max(_InnerFade, 1e-3), r);

                float mark = (teeth + rings) * edge * hub * _Spin;

                float3 rgb = _Color.rgb * _Intensity * mark;
                rgb = CompositeFogAttenuate(rgb, IN.positionWS, IN.fogCoord);
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
