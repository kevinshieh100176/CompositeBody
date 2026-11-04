Shader "CompositeBody/OATrailFlow"
{
    // The glowing trail between the two halves in the ObjectAssembly beat.
    //
    // Drawn on a LineRenderer strip owned by OATrail: UV.x runs 0 at end A to 1 at end B, UV.y
    // runs across the ribbon. Two noise flows ride along it, one travelling away from A and
    // one travelling away from B, so the light reads as coming out of BOTH halves and meeting
    // in the middle -- that meeting band is where the two flows multiply into a brighter core.
    //
    // Each flow is anchored at its own end (distance-from-A for one, distance-from-B for the
    // other, in metres), so moving a half drags its own flow with it instead of sliding the
    // pattern through the other one.
    //
    // FLOW SPEED WITHOUT PHASE JUMPS. The flow phase is time x speed, and both come from the
    // shared clock, which can be thousands of seconds in. Raising the speed directly with
    // intensity would make the phase leap by (delta speed x thousands of seconds) the moment
    // the players moved. So each flow is two layers at FIXED speeds, slow and fast, and
    // intensity crossfades between them; the crossfade is normalised by its RMS weight so the
    // contrast does not sag at the halfway point where two independent fields average out.
    //
    // Time is _TrailTime, written by OATrail from ExperienceClock, never _Time: _Time is local
    // to each machine and the two headsets would watch the flow out of step.
    //
    // BLEND. Premultiplied "soft additive": Blend One OneMinusSrcAlpha, rgb is emission and the
    // output alpha is coverage x (1 - _Additive). _Additive = 1 is pure additive glow; lower
    // values let the ribbon darken what is behind it a little so it still reads against the
    // bright walls. HDR is off in this project, so brightness clamps at 1 -- tune colour and
    // _Brightness, never values above 1.

    Properties
    {
        [Header(Colour)]
        _Color ("Flow Colour (from A)", Color) = (1.0, 0.66, 0.30, 1)
        _ColorB ("Flow Colour (from B)", Color) = (1.0, 0.50, 0.36, 1)
        _CoreColor ("Core Colour", Color) = (1.0, 0.93, 0.78, 1)
        _Additive ("Additive (1) vs Over (0)", Range(0, 1)) = 0.8

        [Header(Flow)]
        _NoiseScale ("Noise Cells per Metre", Range(1, 80)) = 16
        _NoiseScaleAcross ("Noise Cells Across", Range(0.25, 8)) = 1.6
        _FlowSpeed ("Flow Speed (cells per second)", Range(0, 30)) = 4
        _FlowBoost ("Flow Speed at Full Intensity (x)", Range(1, 6)) = 2.6
        _Threshold ("Streak Threshold (far)", Range(0, 0.9)) = 0.44
        _ThresholdNear ("Streak Threshold (near)", Range(0, 0.9)) = 0.26

        [Header(Shape)]
        _EdgeSoftness ("Edge Softness", Range(0.5, 6)) = 1.7
        _EdgeWisp ("Edge Wisp", Range(0, 1)) = 0.35
        _CoreWidth ("Core Width", Range(0.05, 1)) = 0.32
        _EndFade ("End Fade (metres)", Range(0.001, 0.3)) = 0.035
        _MeetWidth ("Meeting Width (fraction of length)", Range(0.02, 0.5)) = 0.17
        _MeetGlow ("Meeting Glow", Range(0, 3)) = 1.3
        _BaseGlow ("Base Glow", Range(0, 1)) = 0.16
        _GrowFromEnds ("Reveal Grows From Ends", Range(0, 1)) = 1

        [Header(Driven by OATrail through a property block)]
        _Intensity ("Intensity (driven)", Range(0, 1)) = 0
        _Visibility ("Visibility (driven)", Range(0, 1)) = 1
        _Brightness ("Brightness (driven)", Range(0, 2)) = 1
        _TrailTime ("Shared Time (driven)", Float) = 0
        _TrailLength ("Length in Metres (driven)", Float) = 1
        _StrandSeed ("Strand Seed (driven)", Float) = 0
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
            Name "TrailFlow"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha
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
            #include "Assets/Shaders/CompositeFog.hlsl"
            #include "OATrailNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _ColorB;
                float4 _CoreColor;
                float _Additive;
                float _NoiseScale;
                float _NoiseScaleAcross;
                float _FlowSpeed;
                float _FlowBoost;
                float _Threshold;
                float _ThresholdNear;
                float _EdgeSoftness;
                float _EdgeWisp;
                float _CoreWidth;
                float _EndFade;
                float _MeetWidth;
                float _MeetGlow;
                float _BaseGlow;
                float _GrowFromEnds;
                float _Intensity;
                float _Visibility;
                float _Brightness;
                float _TrailTime;
                float _TrailLength;
                float _StrandSeed;
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
                float3 positionWS  : TEXCOORD2;
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

            // One flow, travelling AWAY from the end that `fromEnd` is measured from.
            // fromEnd and across are in noise cells. Returns roughly 0..1.
            float Flow(float fromEnd, float across, float seed)
            {
                float slow = _TrailTime * _FlowSpeed;
                float fast = slow * _FlowBoost;

                // fromEnd - phase: a feature at a fixed value moves to larger fromEnd as time
                // passes, i.e. away from its own end and toward the other one.
                float nSlow = OATrailFbm(float2(fromEnd - slow, across + seed));
                float nFast = OATrailFbm(float2(fromEnd * 1.27 - fast, across * 1.15 + seed + 19.1));

                float w = saturate(_Intensity);
                float rms = max(sqrt((1.0 - w) * (1.0 - w) + w * w), 1e-3);
                return ((nSlow - 0.5) * (1.0 - w) + (nFast - 0.5) * w) / rms + 0.5;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float x = IN.uv.x;
                float L = max(_TrailLength, 1e-3);
                float intensity = saturate(_Intensity);

                float cells = L * _NoiseScale;
                float acrossCells = (IN.uv.y - 0.5) * _NoiseScaleAcross;

                float fA = Flow(x * cells, acrossCells, _StrandSeed);
                float fB = Flow((1.0 - x) * cells, acrossCells, _StrandSeed + 41.7);

                // Streaks: a threshold that drops as the halves close, so the flow fills in
                // from scattered wisps into a continuous current.
                float lo = lerp(_Threshold, _ThresholdNear, intensity);
                float hi = lo + 0.4;
                fA = smoothstep(lo, hi, fA);
                fB = smoothstep(lo, hi, fB);

                // Each flow owns its own end and thins out as it crosses into the other's.
                float a = fA * (1.0 - smoothstep(0.2, 1.0, x));
                float b = fB * smoothstep(0.0, 0.8, x);

                // Where the two currents overlap they multiply into the brighter mixing band.
                float meetD = (x - 0.5) / max(_MeetWidth, 1e-3);
                float meet = exp(-meetD * meetD);
                float mixing = fA * fB * meet * _MeetGlow * (0.4 + 0.6 * intensity);

                // Ribbon cross-section. The wisp term breathes the width along the length so the
                // edge of a camera-facing strip never reads as a ruled line.
                float wisp = (OATrailValueNoise(float2(x * cells * 0.6 - _TrailTime * _FlowSpeed * 0.35,
                                                       _StrandSeed * 3.1 + 5.0)) - 0.5) * _EdgeWisp;
                float across = saturate(abs(IN.uv.y - 0.5) * 2.0 + wisp);
                float body = pow(saturate(1.0 - across), _EdgeSoftness);
                float coreW = max(_CoreWidth * lerp(0.7, 1.3, intensity), 1e-3);
                float core = pow(saturate(1.0 - across / coreW), 2.0);

                float energy = _BaseGlow + a + b + mixing;

                float3 flowCol = _Color.rgb * (a + _BaseGlow * (1.0 - x))
                               + _ColorB.rgb * (b + _BaseGlow * x);
                float3 rgb = flowCol * body
                           + _CoreColor.rgb * core * (saturate(a + b) * lerp(0.35, 0.9, intensity) + mixing);

                // Soft at both ends, in metres, so the trail emerges from each half rather than
                // starting as a cut ribbon. On a short trail the two fades overlap and the whole
                // thing thins away, which is what should happen as the halves meet.
                float endU = min(_EndFade / L, 0.3);
                float ends = smoothstep(0.0, endU, x) * smoothstep(0.0, endU, 1.0 - x);

                // Reveal: with _GrowFromEnds at 1 the trail grows out of both halves and meets in
                // the middle as visibility rises; at 0 it is a plain fade.
                const float soft = 0.15;
                float g = saturate(_Visibility) * (0.5 + soft);
                float growA = 1.0 - smoothstep(g - soft, g, x);
                float growB = 1.0 - smoothstep(g - soft, g, 1.0 - x);
                float reveal = lerp(1.0, max(growA, growB), _GrowFromEnds) * smoothstep(0.0, 0.25, _Visibility);

                float mask = ends * reveal;
                float coverage = saturate(energy * body) * mask * IN.color.a * _Color.a;
                rgb *= _Brightness * mask * IN.color.rgb * IN.color.a;

                // Additive light can only be attenuated by distance, never mixed toward the fog
                // colour -- see CompositeFogAttenuate. Run it on white to get the transmittance
                // once and apply it to both the emission and the darkening term.
                half3 transmit = CompositeFogAttenuate(half3(1.0, 1.0, 1.0), IN.positionWS, IN.fogCoord);
                rgb *= transmit;
                coverage *= transmit.x;

                return half4(saturate(rgb), coverage * (1.0 - _Additive));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
