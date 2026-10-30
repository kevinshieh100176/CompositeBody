Shader "CompositeBody/PointCloud"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Range(0, 4)) = 1

        [Header(Size)]
        _PointSize ("Point Size (m)", Range(0.0002, 0.05)) = 0.004
        _MinPixels ("Minimum Size (px)", Range(0, 8)) = 1.5
        [Toggle] _Round ("Round Points", Float) = 1
        _Softness ("Edge Softness", Range(0, 1)) = 0.35

        [Header(Role)]
        [Toggle] _Tinted ("Replace Colour With Tint", Float) = 0
        _Desaturate ("Desaturate", Range(0, 1)) = 0

        [Header(Idle Turbulence)]
        _Phase ("Phase (seconds, driven)", Float) = 0
        _Turbulence ("Amplitude (m)", Range(0, 0.08)) = 0.003
        _TurbulenceScale ("Spatial Scale", Range(0.1, 30)) = 6
        _TurbulenceSpeed ("Speed", Range(0, 6)) = 0.9

        [Header(Reveal)]
        _Reveal ("Reveal", Range(0, 1)) = 1
        _RevealFrom ("Reveal Origin (object space)", Vector) = (0, 0, 0, 0)
        _RevealRadius ("Reveal Radius (m)", Range(0, 4)) = 0

        [Header(Glitch)]
        _Glitch ("Glitch Amount", Range(0, 1)) = 0
        _GlitchSlabs ("Bands Per Metre", Range(2, 160)) = 48
        _GlitchShift ("Band Shift (m)", Range(0, 0.5)) = 0.05
        _GlitchDropout ("Band Dropout", Range(0, 1)) = 0.25
        _GlitchScatter ("Spark Scatter (m)", Range(0, 1)) = 0.18
        _GlitchRate ("Steps Per Second (0 freezes)", Range(0, 30)) = 11
        _GlitchSeed ("Frozen Frame Seed", Range(0, 64)) = 3
        _GlitchChroma ("Chromatic Fringe", Range(0, 1)) = 0.5

        [Header(Dissolve)]
        _Dissolve ("Dissolve", Range(0, 1)) = 0
        _DissolveFrom ("Dissolve Origin (object space)", Vector) = (0, 1, 0, 0)
        _DissolveRadius ("Dissolve Radius (m)", Range(0, 4)) = 1.8
        _DissolveScatter ("Scatter vs Order", Range(0, 1)) = 0.35
        _DissolveDrift ("Drift Distance (m)", Range(0, 3)) = 0.6
        _DissolveDir ("Drift Direction (object space)", Vector) = (0, 1, 0, 0)
        _DissolveSpread ("Drift Turbulence (m)", Range(0, 0.5)) = 0.08
        _DissolveShrink ("Shrink As It Goes", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Plane"
            "IgnoreProjector" = "True"
        }

        // Opaque, with depth, and no alpha blending anywhere -- including through the
        // dissolve. A scan is a surface made of holes, so near points must occlude far ones or
        // the subject's back shows through their chest. A departing point is shrunk and then
        // discarded outright instead of faded: at this density the per-point hash staggers the
        // disappearances finely enough to read as a dissolve, and it costs no sorting, no
        // depth loss, and no second render queue.

        Pass
        {
            Name "PointCloudUnlit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
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
                float _Round;
                float _Softness;
                float _Tinted;
                float _Desaturate;
                float _Phase;
                float _Turbulence;
                float _TurbulenceScale;
                float _TurbulenceSpeed;
                float _Reveal;
                float4 _RevealFrom;
                float _RevealRadius;
                float _Glitch;
                float _GlitchSlabs;
                float _GlitchShift;
                float _GlitchDropout;
                float _GlitchScatter;
                float _GlitchRate;
                float _GlitchSeed;
                float _GlitchChroma;
                float _Dissolve;
                float4 _DissolveFrom;
                float _DissolveRadius;
                float _DissolveScatter;
                float _DissolveDrift;
                float4 _DissolveDir;
                float _DissolveSpread;
                float _DissolveShrink;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                // Corner offset in -1..1, written by PlyImporter's Quads topology. A Points
                // mesh has no UV at all, which arrives here as zero and collapses the quad to
                // the point itself -- so one shader covers both topologies with no keyword and
                // no second material to keep in sync.
                float2 corner     : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 corner      : TEXCOORD0;
                float4 color       : COLOR;
                float  fogCoord    : TEXCOORD1;
                float  alive       : TEXCOORD2;
                float3 positionWS  : TEXCOORD3;   // height fog needs to know where this is
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // Everything per-point is derived from the ORIGINAL object-space position, not
                // from the displaced one. A hash of a moving position changes as the point
                // moves, which would make a point's turn to dissolve arrive at a different
                // moment each frame and the cloud would crawl rather than come apart.
                float3 basePosition = IN.positionOS.xyz;
                float hash = PointHash(basePosition);

                float3 positionOS = basePosition + PointTurbulence(
                    basePosition, hash, _Phase, _Turbulence, _TurbulenceScale, _TurbulenceSpeed);

                // Glitch reads off the ORIGINAL position too, so a band stays the same band
                // while it shimmers -- otherwise the slab boundaries crawl and the tear
                // wanders through the body instead of holding still between steps.
                float3 glitchOffset;
                float glitchDropped;
                float glitchChroma;
                PointGlitch(basePosition, hash, _Phase, _Glitch, _GlitchSlabs, _GlitchShift,
                            _GlitchDropout, _GlitchScatter, _GlitchRate, _GlitchSeed,
                            glitchOffset, glitchDropped, glitchChroma);
                positionOS += glitchOffset;

                float revealOrder = PointSpatialOrder(basePosition, _RevealFrom.xyz, _RevealRadius);
                float revealed = saturate((_Reveal - revealOrder) * 8.0);

                float dissolveOrder = PointSpatialOrder(basePosition, _DissolveFrom.xyz, _DissolveRadius);
                float gone = PointDeparture(dissolveOrder, hash, _Dissolve, _DissolveScatter);

                positionOS += PointDrift(basePosition, _DissolveFrom.xyz, hash, _Phase,
                                         gone, _DissolveDrift, _DissolveDir.xyz, _DissolveSpread);

                VertexPositionInputs posInputs = GetVertexPositionInputs(positionOS);
                float4 positionCS = posInputs.positionCS;

                // Billboard in clip space rather than by building a view-space basis. The
                // offset is in projected units, so it is already correct per eye: in Single
                // Pass Instanced each eye has its own projection, and a quad expanded with a
                // shared view vector would sit at a slightly different depth in each eye and
                // read as a doubled image.
                float size = _PointSize * lerp(1.0, saturate(1.0 - gone), _DissolveShrink);
                float2 extent = size * 0.5 * float2(
                    unity_CameraProjection._m00,
                    unity_CameraProjection._m11);

                // Hold a floor in pixels, so a cloud seen from across the room stays visible
                // instead of thinning into noise as its points fall below one pixel.
                float2 pixels = extent * _ScreenParams.xy;
                float2 minExtent = (_MinPixels * 0.5) / max(_ScreenParams.xy, 1.0);
                extent = max(extent, minExtent * step(pixels, _MinPixels * 0.5));

                positionCS.xy += IN.corner * extent * positionCS.w;

                OUT.positionHCS = positionCS;
                OUT.corner = IN.corner;
                OUT.color = float4(GlitchTint(IN.color.rgb, glitchChroma, _GlitchChroma),
                                   IN.color.a);
                OUT.fogCoord = ComputeFogFactor(posInputs.positionCS.z);
                // The point's own centre, not the expanded quad corner. A sprite is millimetres
                // across and the fog cares about metres.
                OUT.positionWS = posInputs.positionWS;

                // Resolved per point in the vertex stage, never per fragment: a point either
                // exists or it does not, and deciding per fragment would fade a point's own
                // edge before its centre.
                OUT.alive = revealed * (1.0 - step(0.985, gone)) * (1.0 - glitchDropped);

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                if (IN.alive <= 0.001) discard;

                float mask = 1.0;
                if (_Round > 0.5)
                {
                    // Points topology arrives with corner = 0, so this measures zero and the
                    // single pixel is kept. Only an expanded quad is actually shaped.
                    float r = length(IN.corner);
                    mask = 1.0 - smoothstep(1.0 - max(_Softness, 0.001), 1.0, r);
                    if (mask <= 0.004) discard;
                }

                float3 rgb = IN.color.rgb;
                float grey = dot(rgb, float3(0.2126, 0.7152, 0.0722));
                rgb = lerp(rgb, grey.xxx, _Desaturate);
                rgb = lerp(rgb * _Color.rgb, _Color.rgb, _Tinted) * _Intensity;

                rgb = CompositeFogMix(rgb, IN.positionWS, IN.fogCoord);
                return half4(rgb, mask * _Color.a);
            }
            ENDHLSL
        }

        // Depth-only, so the cloud occludes correctly and writes into the depth texture URP
        // hands to other effects. The macros are repeated because a shader whose colour pass
        // is stereo-correct and whose depth pass is not still renders wrongly in a headset.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CompositeFog.hlsl"
            #include "PointCloudCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _PointSize;
                float _MinPixels;
                float _Round;
                float _Softness;
                float _Tinted;
                float _Desaturate;
                float _Phase;
                float _Turbulence;
                float _TurbulenceScale;
                float _TurbulenceSpeed;
                float _Reveal;
                float4 _RevealFrom;
                float _RevealRadius;
                float _Glitch;
                float _GlitchSlabs;
                float _GlitchShift;
                float _GlitchDropout;
                float _GlitchScatter;
                float _GlitchRate;
                float _GlitchSeed;
                float _GlitchChroma;
                float _Dissolve;
                float4 _DissolveFrom;
                float _DissolveRadius;
                float _DissolveScatter;
                float _DissolveDrift;
                float4 _DissolveDir;
                float _DissolveSpread;
                float _DissolveShrink;
            CBUFFER_END

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float2 corner     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionHCS : SV_POSITION;
                float2 corner      : TEXCOORD0;
                float  alive       : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings vertDepth(DepthAttributes IN)
            {
                DepthVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 basePosition = IN.positionOS.xyz;
                float hash = PointHash(basePosition);

                float3 positionOS = basePosition + PointTurbulence(
                    basePosition, hash, _Phase, _Turbulence, _TurbulenceScale, _TurbulenceSpeed);

                float3 glitchOffset;
                float glitchDropped;
                float glitchChroma;
                PointGlitch(basePosition, hash, _Phase, _Glitch, _GlitchSlabs, _GlitchShift,
                            _GlitchDropout, _GlitchScatter, _GlitchRate, _GlitchSeed,
                            glitchOffset, glitchDropped, glitchChroma);
                positionOS += glitchOffset;

                float revealOrder = PointSpatialOrder(basePosition, _RevealFrom.xyz, _RevealRadius);
                float revealed = saturate((_Reveal - revealOrder) * 8.0);

                float dissolveOrder = PointSpatialOrder(basePosition, _DissolveFrom.xyz, _DissolveRadius);
                float gone = PointDeparture(dissolveOrder, hash, _Dissolve, _DissolveScatter);

                positionOS += PointDrift(basePosition, _DissolveFrom.xyz, hash, _Phase,
                                         gone, _DissolveDrift, _DissolveDir.xyz, _DissolveSpread);

                VertexPositionInputs posInputs = GetVertexPositionInputs(positionOS);
                float4 positionCS = posInputs.positionCS;

                float size = _PointSize * lerp(1.0, saturate(1.0 - gone), _DissolveShrink);
                float2 extent = size * 0.5 * float2(
                    unity_CameraProjection._m00,
                    unity_CameraProjection._m11);
                positionCS.xy += IN.corner * extent * positionCS.w;

                OUT.positionHCS = positionCS;
                OUT.corner = IN.corner;
                OUT.alive = revealed * (1.0 - step(0.985, gone)) * (1.0 - glitchDropped);
                return OUT;
            }

            half4 fragDepth(DepthVaryings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                if (IN.alive <= 0.001) discard;
                if (_Round > 0.5 && length(IN.corner) > 1.0) discard;
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
