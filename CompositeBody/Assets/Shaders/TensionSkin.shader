Shader "CompositeBody/TensionSkin"
{
    Properties
    {
        [Header(Fabric)]
        _FabricColor ("Fabric Colour", Color) = (0.09, 0.10, 0.13, 1)
        _SkinTint ("Skin Bleed Tint", Color) = (0.85, 0.56, 0.44, 1)
        _BaseThickness ("Base Thickness", Range(0.05, 4)) = 1.6
        _Density ("Fibre Density", Range(0.1, 8)) = 2.4
        _SheerFloor ("Minimum Opacity", Range(0, 1)) = 0.05

        [Header(Tension Response)]
        _TensionGain ("Tension Gain", Range(0.5, 24)) = 8
        _TensionMax ("Tension Clamp", Range(1.05, 8)) = 3
        _SkinBleed ("Skin Bleed Through", Range(0, 2)) = 0.25

        [Header(Knit)]
        _KnitScale ("Knit Scale", Float) = 420
        _YarnWidth ("Yarn Width", Range(0.05, 0.99)) = 0.94
        _KnitSoftness ("Knit Softness", Range(0.01, 0.5)) = 0.14
        _KnitRelief ("Knit Relief", Range(0, 1)) = 0.12

        [Header(Compression Bunching)]
        _BunchStrength ("Bunching Wrinkles", Range(0, 1)) = 0.55
        _BunchScale ("Bunching Scale", Float) = 60
        _BunchShade ("Crease Shading", Range(0, 1)) = 0.45

        [Header(Surface)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.30
        _TensionGloss ("Tension Gloss", Range(0, 1)) = 0.45
        _SpecIntensity ("Specular Intensity", Range(0, 4)) = 0.45
        _SheenColor ("Sheen Colour", Color) = (0.56, 0.55, 0.57, 1)
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3.2
        _FresnelIntensity ("Fresnel Intensity", Range(0, 3)) = 0.25
        _AmbientIntensity ("Ambient Intensity", Range(0, 2)) = 0.6

        [Header(Debug)]
        [Toggle] _ShowTension ("Visualise Tension (red stretched blue bunched)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // Premultiplied alpha, matching the goo shell: the sheen and rim on taut fabric
            // have to survive the sheet going sheer. With straight SrcAlpha blending the
            // highlight fades out exactly where the fabric is tightest and glossiest, which is
            // backwards.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _FabricColor;
                float4 _SkinTint;
                float _BaseThickness;
                float _Density;
                float _SheerFloor;
                float _TensionGain;
                float _TensionMax;
                float _SkinBleed;
                float _KnitScale;
                float _YarnWidth;
                float _KnitSoftness;
                float _KnitRelief;
                float _BunchStrength;
                float _BunchScale;
                float _BunchShade;
                float _Smoothness;
                float _TensionGloss;
                float _SpecIntensity;
                float4 _SheenColor;
                float _FresnelPower;
                float _FresnelIntensity;
                float _AmbientIntensity;
                float _ShowTension;
            CBUFFER_END

            // ---- Knit ---------------------------------------------------------------------
            // Two arcs per cell, courses staggered half a cell row to row, which is how knit
            // loops actually interlock. Cheaper than a texture fetch and, more usefully, it is
            // defined in UV space -- welded to the fabric -- so the pattern grows with the
            // surface on its own when the body stretches it.
            float TensionKnitField(float2 k)
            {
                k.x += 0.5 * floor(k.y);
                float2 f = frac(k);
                float bow = 0.30 * cos(f.x * 2.0 * PI);
                float d = min(abs(f.y - (0.5 + bow)), abs(f.y - (0.5 - bow)));
                return 1.0 - saturate(d * 4.0);
            }

            // Cheap value noise for the buckling creases, same shape as the one in ClothDrape.
            float TensionHash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
            }

            float TensionNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = TensionHash(i);
                float b = TensionHash(i + float2(1, 0));
                float c = TensionHash(i + float2(0, 1));
                float d = TensionHash(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;   // skinned garment surface
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                float3 restPos    : TEXCOORD2;  // the garment's own bind pose, from TensionSkinBuilder
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 tangentWS   : TEXCOORD2;
                float3 bitangentWS : TEXCOORD3;
                float2 uv          : TEXCOORD4;
                float3 restWS      : TEXCOORD5; // bind pose through the same object matrix
                float  fogCoord    : TEXCOORD6;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nrmInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS = nrmInputs.normalWS;
                OUT.tangentWS = nrmInputs.tangentWS;
                OUT.bitangentWS = nrmInputs.bitangentWS;
                OUT.uv = IN.uv;

                // Put the rest pose through the same object-to-world transform as the live
                // surface, so a scaled avatar cancels out of the ratio taken in the fragment
                // stage instead of reading as permanent stretch.
                OUT.restWS = TransformObjectToWorld(IN.restPos);

                OUT.fogCoord = ComputeFogFactor(posInputs.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 T = normalize(IN.tangentWS);
                float3 B = normalize(IN.bitangentWS);
                float3 V = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                // ---- Tension ---------------------------------------------------------------
                // Area Jacobian of (bind pose -> skinned pose), read straight off the
                // rasteriser. The same screen quad covers both surfaces, so perspective and
                // camera distance cancel in the ratio and what is left is purely how far the
                // fabric has been stretched at this pixel. No CPU work, no per-frame upload,
                // and it picks up anything that moves vertices -- skinning, blendshapes, a
                // cloth solver -- without having to be told about it.
                float3 dPx = ddx(IN.positionWS);
                float3 dPy = ddy(IN.positionWS);
                float3 dRx = ddx(IN.restWS);
                float3 dRy = ddy(IN.restWS);

                // A mesh with no rest channel reports zero rest area; falling back to 1.0 makes
                // the garment render as plain untensioned fabric instead of garbage. Clamping
                // the ratio keeps sub-pixel and degenerate triangles -- fingers, toes, the
                // inside of the mouth -- from flaring to full tension just because their
                // screen-space derivatives are meaningless at that size.
                float areaRest = length(cross(dRx, dRy));
                float areaRatio = (areaRest > 1e-12) ? length(cross(dPx, dPy)) / areaRest : 1.0;
                areaRatio = clamp(areaRatio, 0.25, 4.0);

                // A bent knee only opens knit by a few tens of percent in area; left raw that
                // is nearly invisible, so the gain is what turns real deformation into
                // something you can actually see.
                float stretchArea = clamp(1.0 + (areaRatio - 1.0) * _TensionGain, 0.05, _TensionMax);
                float tension = saturate((stretchArea - 1.0) / max(_TensionMax - 1.0, 1e-3));
                float compress = saturate(1.0 - stretchArea);
                float linStretch = sqrt(stretchArea);

                // ---- Knit coverage ---------------------------------------------------------
                // Yarn thins as it is pulled, so the gaps between loops widen. The pattern is
                // already growing with the surface because it lives in UV space; what tension
                // adds on top is the yarn getting narrower inside each growing cell. That is
                // the difference between fabric going sheer and fabric being faded out.
                float2 kuv = IN.uv * _KnitScale;

                // A procedural pattern has no mip chain, so once the loops approach pixel size
                // they alias into crawling glitter. Measuring the cell's screen footprint and
                // fading the weave toward its own average as that approaches a pixel is what
                // keeps the fabric smooth at a distance -- and it degrades to a plain uniform
                // thinning, which is still the right answer.
                float footprint = max(length(ddx(kuv)), length(ddy(kuv)));
                float knitFade = saturate(1.2 - footprint * 2.0);

                // At rest the yarn all but fills its cell, so the fabric is solid; the gaps
                // only open as the loops are pulled apart. Starting with visible holes would
                // read as netting rather than as knit that has been stretched sheer.
                float yarnW = saturate(_YarnWidth / linStretch);
                float knit = TensionKnitField(kuv);
                float coverage = smoothstep(1.0 - yarnW - _KnitSoftness,
                                            1.0 - yarnW + _KnitSoftness, knit);
                coverage = lerp(saturate(yarnW), coverage, knitFade);

                // ---- Opacity ---------------------------------------------------------------
                // The sheet conserves volume, so thickness falls as area grows, and
                // Beer-Lambert on that thickness is what turns "pulled tight" into "you can
                // see through it" on a curve rather than a straight fade.
                float thickness = _BaseThickness / max(stretchArea, 1e-3);
                float bulk = 1.0 - exp(-_Density * thickness);
                bulk = max(bulk, _SheerFloor);

                float alpha = saturate(bulk * coverage);

                // ---- Creasing where the sheet is shortening --------------------------------
                // Compressed cloth has nowhere to go but out of plane, so it buckles. Driven
                // off compression alone, which is why the folds land in the crook of a bent
                // joint instead of over the whole limb.
                float crease = 0.0;
                if (_BunchStrength > 0.001 && compress > 0.001)
                {
                    float2 bp = IN.uv * _BunchScale;
                    float n0 = TensionNoise(bp);
                    float e = 0.02;
                    float2 g = float2(TensionNoise(bp + float2(e, 0)) - n0,
                                      TensionNoise(bp + float2(0, e)) - n0) / e;
                    N = normalize(N - (T * g.x + B * g.y) * _BunchStrength * compress * 0.03);
                    crease = saturate((0.5 - n0) * 2.0) * compress;
                }

                // Knit relief: the yarn stands proud of the gaps, so the loops catch light.
                // Faded out on the same footprint as the coverage -- per-loop normals that are
                // finer than a pixel turn the whole garment into sparkling chainmail.
                float relief = _KnitRelief * knitFade;
                if (relief > 0.001)
                {
                    float k0 = knit;
                    float e = 0.06;
                    float2 gk = float2(TensionKnitField(kuv + float2(e, 0)) - k0,
                                       TensionKnitField(kuv + float2(0, e)) - k0) / e;
                    N = normalize(N - (T * gk.x + B * gk.y) * relief * 0.004);
                }

                if (_ShowTension > 0.5)
                {
                    // Grey at rest, red where the fabric is stretched, blue where it bunches.
                    float3 dbg = lerp(float3(0.42, 0.42, 0.45), float3(0.95, 0.18, 0.12), tension);
                    dbg = lerp(dbg, float3(0.15, 0.35, 0.95), compress);
                    return half4(dbg, 1.0);
                }

                // ---- Shading ---------------------------------------------------------------
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float atten = mainLight.distanceAttenuation * mainLight.shadowAttenuation;

                float NoL = dot(N, mainLight.direction);
                float NoV = saturate(dot(N, V)) + 1e-4;

                // Taut fabric flattens against the body and polishes up; slack fabric is
                // fuzzier. Creases sit in their own shadow.
                float smoothness = saturate(_Smoothness + tension * _TensionGloss);
                float roughness = max(1.0 - smoothness, 0.03);
                roughness *= roughness;

                float3 albedo = _FabricColor.rgb * (1.0 - crease * _BunchShade);

                // Wrapped diffuse: a knit over skin has enough scatter that a hard terminator
                // reads as plastic.
                float wrapped = saturate((NoL + 0.3) / 1.3);
                float3 color = albedo * wrapped * mainLight.color * atten;

                float3 H = SafeNormalize(mainLight.direction + V);
                float NoH = saturate(dot(N, H));
                float VoH = saturate(dot(V, H));

                float a2 = roughness * roughness;
                float dDen = NoH * NoH * (a2 - 1.0) + 1.0;
                float D = a2 / max(PI * dDen * dDen, 1e-6);
                float Vis = 0.5 / max(NoV + saturate(NoL), 1e-4);
                float3 F = _SheenColor.rgb + (1.0 - _SheenColor.rgb) * pow(1.0 - VoH, 5.0);
                float3 spec = D * Vis * F * _SpecIntensity * saturate(NoL) * mainLight.color * atten;

                float3 ambient = SampleSH(N) * _AmbientIntensity;
                color += albedo * ambient;

                // Premultiply the body of the fabric, then add the highlights at full strength
                // so they survive the sheet going sheer.
                float3 outRGB = color * alpha;

                float fresnel = pow(1.0 - NoV, _FresnelPower);
                outRGB += spec;
                outRGB += _SheenColor.rgb * fresnel * _FresnelIntensity * alpha;

                // Warm bounce picked up inside the fibres from the skin behind them. Added
                // after the premultiply and kept small on purpose: the blend already shows the
                // real body through (1 - alpha), so this is only the glow within the fabric
                // itself. Folding it in before the premultiply, as an earlier pass did, scaled
                // it by alpha as well and made it peak in the half-sheer midtones instead of
                // where the fabric is thinnest.
                outRGB += _SkinTint.rgb * _SkinBleed * (1.0 - alpha)
                          * (ambient + mainLight.color * saturate(NoL) * atten * 0.35);

                outRGB = MixFog(outRGB, IN.fogCoord);
                return half4(outRGB, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
