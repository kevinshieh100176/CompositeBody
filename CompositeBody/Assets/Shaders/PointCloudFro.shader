Shader "CompositeBody/PointCloudFro"
{
    // A point cloud in the manner of RubenFro's volumetric work: a captured figure that holds
    // together at a distance and comes apart as you walk up to it, points cascading downward
    // while they brighten.
    //
    // Why this is a separate shader and not a mode on CompositeBody/PointCloud: the two
    // disagree about the most basic thing a shader does. The other one is opaque and writes
    // depth, because a scan is a surface full of holes and near points have to occlude far
    // ones. This one is additive and writes no depth, because the look depends on hundreds of
    // faint overlapping points accumulating into light. Those cannot be a keyword apart.
    //
    // The signature is a mechanic, not a finish. Proximity drives the disintegration, so the
    // figure is destroyed by being approached rather than on a cue -- which is the right
    // behaviour for the 真人, who the script has the players walk toward and reach for while
    // the memory refuses to be held. A cue is still available through _Cascade for the beats
    // that need to drive it directly.

    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Range(0, 6)) = 1.4
        _PointSize ("Point Size (m)", Range(0.0002, 0.05)) = 0.004
        _MinPixels ("Minimum Size (px)", Range(0, 8)) = 1.2
        _Softness ("Edge Softness", Range(0, 1)) = 0.6

        [Header(Approach)]
        _ApproachNear ("Fully Gone Within (m)", Range(0.05, 6)) = 0.45
        _ApproachFar ("Intact Beyond (m)", Range(0.1, 20)) = 2.6
        _Cascade ("Cascade Override", Range(0, 1)) = 0
        _ApproachCurve ("Approach Curve", Range(0.25, 4)) = 1.6

        [Header(Cascade)]
        _Phase ("Phase (seconds, driven)", Float) = 0
        _FallDistance ("Fall Distance (m)", Range(0, 4)) = 1.1
        _FallSpread ("Fall Wander (m)", Range(0, 1)) = 0.22
        _LumiLead ("Luminance Leads", Range(-1, 1)) = 0.55
        _DensityLead ("Sparse Leads", Range(0, 1)) = 0.7
        _CascadeJitter ("Per Point Jitter", Range(0, 1)) = 0.45

        [Header(Burn)]
        _Burn ("Burn Brightness", Range(0, 8)) = 2.6
        _BurnColor ("Burn Colour", Color) = (1, 0.72, 0.42, 1)
        _BurnSharp ("Burn Sharpness", Range(0.2, 6)) = 2

        [Header(Atmosphere)]
        _DepthFade ("Distance Fade (m)", Range(0, 40)) = 14
        _NearFade ("Near Fade (m)", Range(0, 2)) = 0.18

        [Header(Idle)]
        _Turbulence ("Amplitude (m)", Range(0, 0.08)) = 0.002
        _TurbulenceScale ("Spatial Scale", Range(0.1, 30)) = 6
        _TurbulenceSpeed ("Speed", Range(0, 6)) = 0.7

        [Header(Reveal)]
        _Reveal ("Reveal", Range(0, 1)) = 1
        _RevealFrom ("Reveal Origin (object space)", Vector) = (0, 0, 0, 0)
        _RevealRadius ("Reveal Radius (m)", Range(0, 4)) = 0
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
            Name "PointCloudFro"
            Tags { "LightMode" = "UniversalForward" }

            // Additive, and no depth write. Sorting is therefore irrelevant, which is the
            // point: at seven hundred thousand points no sort would be affordable anyway, and
            // the accumulation is what produces the glow through the body.
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CompositeFog.hlsl"
            #include "PointCloudCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _PointSize;
                float _MinPixels;
                float _Softness;
                float _ApproachNear;
                float _ApproachFar;
                float _Cascade;
                float _ApproachCurve;
                float _Phase;
                float _FallDistance;
                float _FallSpread;
                float _LumiLead;
                float _DensityLead;
                float _CascadeJitter;
                float _Burn;
                float4 _BurnColor;
                float _BurnSharp;
                float _DepthFade;
                float _NearFade;
                float _Turbulence;
                float _TurbulenceScale;
                float _TurbulenceSpeed;
                float _Reveal;
                float4 _RevealFrom;
                float _RevealRadius;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 corner     : TEXCOORD0;   // quad corner, zero on a Points mesh
                float2 attrib     : TEXCOORD1;   // x density, y luminance, baked by PlyImporter
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 corner      : TEXCOORD0;
                float4 color       : COLOR;
                float  fogCoord    : TEXCOORD1;
                float  fall        : TEXCOORD2;
                float  fade        : TEXCOORD3;
                float3 positionWS  : TEXCOORD4;   // height fog needs to know where this is
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 basePosition = IN.positionOS.xyz;
                float hash = PointHash(basePosition);

                // Luminance from the baked attribute when the importer wrote one, otherwise
                // straight off the colour. Either way it is the point's own brightness, which
                // is what orders the cascade.
                float luminance = IN.attrib.y > 0.0
                    ? IN.attrib.y
                    : dot(IN.color.rgb, float3(0.2126, 0.7152, 0.0722));
                // No baked density reads as "fully crowded", so an unbaked cloud cascades on
                // luminance alone instead of treating every point as an isolated speck.
                float density = IN.attrib.x > 0.0 ? IN.attrib.x : 1.0;

                float3 positionOS = basePosition + PointTurbulence(
                    basePosition, hash, _Phase, _Turbulence, _TurbulenceScale, _TurbulenceSpeed);

                // --- how close the viewer is to THIS point -------------------------------
                // Measured per point rather than to the object's pivot, so walking up to a
                // figure's hand takes the hand apart first and leaves the rest standing. That
                // local behaviour is most of what sells the effect.
                float3 positionWS = TransformObjectToWorld(positionOS);
                float toCamera = distance(positionWS, _WorldSpaceCameraPos);
                float approach = saturate((_ApproachFar - toCamera) / max(_ApproachFar - _ApproachNear, 0.001));
                approach = pow(approach, _ApproachCurve);

                float cascade = saturate(max(approach, _Cascade));

                // --- who goes first ------------------------------------------------------
                // Sparse points before solid ones, and bright before dark, with a per-point
                // jitter so no band leaves as a sheet. Thresholds, not a uniform fade: every
                // point has its own moment.
                float order = lerp(0.0, 1.0 - density, _DensityLead);
                order += (luminance - 0.5) * -_LumiLead;
                order = lerp(order, hash, _CascadeJitter);
                order = saturate(order);

                float fall = saturate((cascade - order * 0.85) / 0.3);

                // --- the fall ------------------------------------------------------------
                // Accelerating, because points that depart at a constant rate read as rising
                // smoke rather than as something giving way.
                float travel = fall * fall * _FallDistance;
                float3 wander = PointTurbulence(basePosition * 2.3, hash, _Phase,
                                                _FallSpread, 1.9, 0.8) * fall;
                positionOS.y -= travel;
                positionOS += wander;

                float revealOrder = PointSpatialOrder(basePosition, _RevealFrom.xyz, _RevealRadius);
                float revealed = saturate((_Reveal - revealOrder) * 8.0);

                VertexPositionInputs posInputs = GetVertexPositionInputs(positionOS);
                float4 positionCS = posInputs.positionCS;

                float2 extent = _PointSize * 0.5 * float2(
                    unity_CameraProjection._m00,
                    unity_CameraProjection._m11);
                float2 pixels = extent * _ScreenParams.xy;
                float2 minExtent = (_MinPixels * 0.5) / max(_ScreenParams.xy, 1.0);
                extent = max(extent, minExtent * step(pixels, _MinPixels * 0.5));
                positionCS.xy += IN.corner * extent * positionCS.w;

                OUT.positionHCS = positionCS;
                OUT.corner = IN.corner;
                OUT.fogCoord = ComputeFogFactor(posInputs.positionCS.z);
                OUT.positionWS = posInputs.positionWS;
                OUT.fall = fall;

                // --- burn ----------------------------------------------------------------
                // Brightening as it falls is the part that makes this read as burning rather
                // than as debris. The colour is pushed toward the burn tint at the same time,
                // so a departing point stops belonging to the body it came from.
                float burn = pow(fall, _BurnSharp);
                float3 rgb = lerp(IN.color.rgb, _BurnColor.rgb, saturate(burn * 0.9));
                rgb *= 1.0 + burn * _Burn;
                OUT.color = float4(rgb * _Color.rgb, IN.color.a);

                // Fade out far away so the cloud sinks into the air instead of ending at a
                // hard edge, and fade out very near so points do not flare across the whole
                // view as the player's head passes through the figure.
                float far = _DepthFade > 0.001 ? saturate(1.0 - toCamera / _DepthFade) : 1.0;
                float near = _NearFade > 0.001 ? saturate(toCamera / _NearFade) : 1.0;
                OUT.fade = far * near * revealed * (1.0 - saturate((fall - 0.85) / 0.15));

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                if (IN.fade <= 0.002) discard;

                // Soft round falloff rather than a flat disc. Additive points want a core that
                // accumulates and an edge that does not, or the cloud turns into a sheet of
                // overlapping hard circles.
                float r = length(IN.corner);
                float shape = pow(saturate(1.0 - r), max(_Softness, 0.001) * 4.0);
                if (shape <= 0.002) discard;

                float3 rgb = IN.color.rgb * _Intensity;
                rgb = CompositeFogMix(rgb, IN.positionWS, IN.fogCoord);
                return half4(rgb, shape * IN.fade * _Color.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
