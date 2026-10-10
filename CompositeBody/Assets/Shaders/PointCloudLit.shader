Shader "CompositeBody/PointCloudLit"
{
    // A white point cloud that takes its colour from the light falling on it, rather than from
    // the scan.
    //
    // This is the variant the script actually asks for in S3-1: 「紫區真人B、黃區真人A」, the
    // figures standing in their own coloured 光區. A cloud rendered in its captured colour
    // fights the zone it is standing in -- a maroon top stays maroon under purple light. A
    // white one does not have an opinion, so the zone decides, and a figure that walks from one
    // zone to the other changes colour because the light changed. That is one less thing to
    // author and one less thing to keep in sync with the lighting design.
    //
    // It also solves the scan's biggest weakness. These captures are nearly colourless -- the
    // figure here averages RGB 30,20,24, almost black, and no amount of grading recovers detail
    // that was never recorded. Lighting puts the form back: shape read from shading instead of
    // from albedo.
    //
    // Needs normals, which a scan does not come with. PlyImporter's Estimate Normals fits them
    // from each point's neighbourhood; without it every normal is 0,1,0 and the figure lights
    // like a flat lid.

    Properties
    {
        _BaseColor ("Albedo", Color) = (1, 1, 1, 1)
        _Exposure ("Exposure", Range(0, 4)) = 1

        [Header(Shading)]
        _Wrap ("Light Wrap", Range(0, 1)) = 0.35
        _AmbientBoost ("Ambient Boost", Range(0, 3)) = 1
        _Specular ("Specular", Range(0, 2)) = 0.25
        _Smoothness ("Smoothness", Range(0.02, 1)) = 0.35
        [Toggle] _KeepScanColor ("Keep Scan Colour", Float) = 0
        _ScanColorMix ("Scan Colour Mix", Range(0, 1)) = 0.25

        [Header(Size)]
        _PointSize ("Point Size (m)", Range(0.0002, 0.05)) = 0.0045
        _MinPixels ("Minimum Size (px)", Range(0, 8)) = 1.5
        [Toggle] _Round ("Round Points", Float) = 1
        _Softness ("Edge Softness", Range(0, 1)) = 0.3

        [Header(Idle Turbulence)]
        _Phase ("Phase (seconds, driven)", Float) = 0
        _Turbulence ("Amplitude (m)", Range(0, 0.08)) = 0.003
        _TurbulenceScale ("Spatial Scale", Range(0.1, 30)) = 6
        _TurbulenceSpeed ("Speed", Range(0, 6)) = 0.9

        [Header(Glitch)]
        _Glitch ("Glitch Amount", Range(0, 1)) = 0
        _GlitchSlabs ("Bands Per Metre", Range(2, 160)) = 48
        _GlitchShift ("Band Shift (m)", Range(0, 0.5)) = 0.05
        _GlitchDropout ("Band Dropout", Range(0, 1)) = 0.25
        _GlitchScatter ("Spark Scatter (m)", Range(0, 1)) = 0.18
        _GlitchRate ("Steps Per Second (0 freezes)", Range(0, 30)) = 11
        _GlitchSeed ("Frozen Frame Seed", Range(0, 64)) = 3

        [Header(Reveal)]
        _Reveal ("Reveal", Range(0, 1)) = 1
        _RevealFrom ("Reveal Origin (object space)", Vector) = (0, 0, 0, 0)
        _RevealRadius ("Reveal Radius (m)", Range(0, 4)) = 0

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

        Pass
        {
            Name "PointCloudLitForward"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            // Receiving only. A ShadowCaster pass would mean rasterising seven hundred thousand
            // points again per cascade, and a cloud casting its own holes onto itself reads as
            // dirt rather than as shadow. The zones light the figure; the figure does not
            // occlude them.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CompositeFog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "PointCloudCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Exposure;
                float _Wrap;
                float _AmbientBoost;
                float _Specular;
                float _Smoothness;
                float _KeepScanColor;
                float _ScanColorMix;
                float _PointSize;
                float _MinPixels;
                float _Round;
                float _Softness;
                float _Phase;
                float _Turbulence;
                float _TurbulenceScale;
                float _TurbulenceSpeed;
                float _Glitch;
                float _GlitchSlabs;
                float _GlitchShift;
                float _GlitchDropout;
                float _GlitchScatter;
                float _GlitchRate;
                float _GlitchSeed;
                float _Reveal;
                float4 _RevealFrom;
                float _RevealRadius;
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
                float3 normalOS   : NORMAL;
                float2 corner     : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 corner      : TEXCOORD0;
                float4 color       : COLOR;
                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                float  fogCoord    : TEXCOORD3;
                float  alive       : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
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
                float2 pixels = extent * _ScreenParams.xy;
                float2 minExtent = (_MinPixels * 0.5) / max(_ScreenParams.xy, 1.0);
                extent = max(extent, minExtent * step(pixels, _MinPixels * 0.5));
                positionCS.xy += IN.corner * extent * positionCS.w;

                OUT.positionHCS = positionCS;
                OUT.corner = IN.corner;
                OUT.color = IN.color;
                OUT.positionWS = posInputs.positionWS;

                // Normals are rotated but NOT displaced with the point. A glitched or dissolving
                // point keeps the orientation it had on the surface it came from, which keeps
                // the shading coherent while the geometry comes apart -- the alternative, some
                // normal derived from the drift, makes a dissolving figure flicker.
                OUT.normalWS = normalize(TransformObjectToWorldNormal(IN.normalOS));

                OUT.fogCoord = ComputeFogFactor(posInputs.positionCS.z);
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
                    float r = length(IN.corner);
                    mask = 1.0 - smoothstep(1.0 - max(_Softness, 0.001), 1.0, r);
                    if (mask <= 0.004) discard;
                }

                // A point has no inside, so a normal facing away from the camera is a point on
                // the far side of the body that nothing is hiding -- flipping it keeps those
                // from reading as black holes in the surface.
                float3 normalWS = normalize(IN.normalWS);
                float3 viewWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                if (dot(normalWS, viewWS) < 0.0) normalWS = -normalWS;

                float3 albedo = lerp(_BaseColor.rgb,
                                     _BaseColor.rgb * IN.color.rgb,
                                     _KeepScanColor > 0.5 ? 1.0 : _ScanColorMix);

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                // Wrapped diffuse. A scan's normals are noisy at this density, and a hard
                // N.L terminator turns that noise into speckle right where the form reads.
                // Wrapping pushes the terminator around the back and keeps the shading smooth.
                float wrap = max(_Wrap, 0.001);
                float ndotl = saturate((dot(normalWS, mainLight.direction) + wrap) / (1.0 + wrap));
                float3 lighting = mainLight.color * (ndotl * mainLight.shadowAttenuation *
                                                     mainLight.distanceAttenuation);

                // Specular kept subtle and wide. The 真人 should read as damp rather than
                // polished, and a tight highlight on a point cloud only ever looks like noise.
                float3 halfVector = normalize(mainLight.direction + viewWS);
                float gloss = exp2(_Smoothness * 9.0 + 1.0);
                float spec = pow(saturate(dot(normalWS, halfVector)), gloss) * _Specular;
                lighting += mainLight.color * spec * mainLight.shadowAttenuation;

                #ifdef _ADDITIONAL_LIGHTS
                // This is what carries the 光區: the purple and yellow zones are point lights,
                // and a white figure standing in one comes out that colour with nothing
                // authored per figure.
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light light = GetAdditionalLight(lightIndex, IN.positionWS, half4(1, 1, 1, 1));
                    float add = saturate((dot(normalWS, light.direction) + wrap) / (1.0 + wrap));
                    lighting += light.color * (add * light.distanceAttenuation *
                                               light.shadowAttenuation);
                LIGHT_LOOP_END
                #endif

                float3 ambient = SampleSH(normalWS) * _AmbientBoost;
                float3 rgb = albedo * (lighting + ambient) * _Exposure;

                rgb = CompositeFogMix(rgb, IN.positionWS, IN.fogCoord);
                return half4(rgb, mask * _BaseColor.a);
            }
            ENDHLSL
        }

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
                float4 _BaseColor;
                float _Exposure;
                float _Wrap;
                float _AmbientBoost;
                float _Specular;
                float _Smoothness;
                float _KeepScanColor;
                float _ScanColorMix;
                float _PointSize;
                float _MinPixels;
                float _Round;
                float _Softness;
                float _Phase;
                float _Turbulence;
                float _TurbulenceScale;
                float _TurbulenceSpeed;
                float _Glitch;
                float _GlitchSlabs;
                float _GlitchShift;
                float _GlitchDropout;
                float _GlitchScatter;
                float _GlitchRate;
                float _GlitchSeed;
                float _Reveal;
                float4 _RevealFrom;
                float _RevealRadius;
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
