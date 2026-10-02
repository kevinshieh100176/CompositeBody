Shader "CompositeBody/ClothDrape"
{
    Properties
    {
        [Header(Base Cloth)]
        _BaseColor ("Base Color", Color) = (0.014, 0.014, 0.016, 1)
        _DiffuseWrap ("Diffuse Wrap", Range(0, 1)) = 0.35

        [Header(Satin Sheen)]
        _SheenColor ("Sheen Color", Color) = (0.30, 0.31, 0.35, 1)
        _SheenRoughness ("Sheen Roughness", Range(0.05, 1)) = 0.48
        _SheenIntensity ("Sheen Intensity", Range(0, 4)) = 1.35

        [Header(Anisotropic Silk Highlight)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.50
        _Anisotropy ("Anisotropy", Range(-1, 1)) = 0.62
        _SpecColor2 ("Specular Color", Color) = (0.85, 0.86, 0.90, 1)
        _SpecIntensity ("Specular Intensity", Range(0, 4)) = 0.28
        _RoughnessBreakup ("Roughness Breakup", Range(0, 1)) = 0.55

        [Header(Grazing Rim)]
        _FresnelColor ("Fresnel Color", Color) = (0.42, 0.45, 0.50, 1)
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3.6
        _FresnelIntensity ("Fresnel Intensity", Range(0, 3)) = 0.32

        [Header(Micro Wrinkles)]
        _WrinkleStrength ("Wrinkle Strength", Range(0, 1)) = 0.45
        _WrinkleScale ("Wrinkle Scale", Float) = 34
        _WrinkleStretch ("Wrinkle Vertical Stretch", Range(1, 16)) = 7
        [Toggle] _WrinkleCylindrical ("Cylindrical Wrinkles (seamless on closed garments)", Float) = 0

        [Header(Tearing)]
        _TearAmount ("Tear Amount", Range(0, 1)) = 0
        _TearScale ("Tear Scale", Float) = 9
        _TearElongation ("Tear Elongation Along Grain", Range(1, 16)) = 6
        _TearOrigin ("Tear Origin (0 hem to 1 crown)", Range(0, 1)) = 0
        _TearSpread ("Tear Spread", Range(0.05, 1.5)) = 0.8
        _TearCurl ("Torn Edge Curl", Range(0, 1)) = 0.45
        _TearTension ("Tension Whitening", Range(0, 1)) = 0.35
        [Toggle] _TearWrapU ("Wrap Tears Around U (closed garments)", Float) = 1

        [Header(Frayed Edges)]
        _FrayWidth ("Fray Width", Range(0, 1)) = 0.35
        _FrayScale ("Fray Thread Scale", Float) = 220
        _FrayColor ("Loose Fibre Color", Color) = (0.42, 0.41, 0.39, 1)
        _FrayDensity ("Loose Fibre Density", Range(0, 1)) = 0.80
        _FraySheen ("Loose Fibre Sheen", Range(0, 4)) = 1.8

        [Header(Torn Interior)]
        _InteriorShade ("Interior Shade", Range(0, 1)) = 0.45

        [Header(Ambient)]
        _AmbientIntensity ("Ambient Intensity", Range(0, 2)) = 0.38
    }

    SubShader
    {
        // Cutout rather than plain opaque: the tear is an alpha test, so the garment has to be
        // classified and sorted with the rest of the clipped geometry.
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }

        // Shared by every pass. The tear has to be evaluated in the shadow and depth passes as
        // well as the lit one -- a hole that still casts a solid shadow gives itself away
        // immediately -- and three copies of the noise stack is not worth maintaining.
        HLSLINCLUDE

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _DiffuseWrap;
            float4 _SheenColor;
            float _SheenRoughness;
            float _SheenIntensity;
            float _Smoothness;
            float _Anisotropy;
            float4 _SpecColor2;
            float _SpecIntensity;
            float _RoughnessBreakup;
            float4 _FresnelColor;
            float _FresnelPower;
            float _FresnelIntensity;
            float _WrinkleStrength;
            float _WrinkleScale;
            float _WrinkleStretch;
            float _WrinkleCylindrical;
            float _TearAmount;
            float _TearScale;
            float _TearElongation;
            float _TearOrigin;
            float _TearSpread;
            float _TearCurl;
            float _TearTension;
            float _TearWrapU;
            float _FrayWidth;
            float _FrayScale;
            float4 _FrayColor;
            float _FrayDensity;
            float _FraySheen;
            float _InteriorShade;
            float _AmbientIntensity;
        CBUFFER_END

        // ---- Procedural noise ---------------------------------------------------------
        // Cheap value noise; the fragment stage takes finite differences of this to bend
        // the shading normal, so the silk picks up fine creases without a normal map.
        float ClothHash(float2 p)
        {
            return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
        }

        // Lattice coordinates wrap in X, so the field is seamless where U wraps from 1 back
        // to 0 -- without this a garment modelled as a closed cylinder shows a hard seam
        // line down the wrap.
        float ClothValueNoise(float2 p, float tileX)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);

            float ix0 = fmod(i.x + tileX, tileX);
            float ix1 = fmod(i.x + 1.0 + tileX, tileX);

            float a = ClothHash(float2(ix0, i.y));
            float b = ClothHash(float2(ix1, i.y));
            float c = ClothHash(float2(ix0, i.y + 1.0));
            float d = ClothHash(float2(ix1, i.y + 1.0));
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }

        float ClothWrinkleField(float2 uv)
        {
            // Stretched along V so creases run with the drape rather than looking like static.
            // Octaves step by whole multiples to keep every level tiling in U.
            float tile = max(1.0, floor(_WrinkleScale + 0.5));
            float2 p = float2(uv.x * tile, uv.y * (tile / max(_WrinkleStretch, 1.0)));
            float n = ClothValueNoise(p, tile) * 0.6;
            n += ClothValueNoise(p * 2.0, tile * 2.0) * 0.3;
            n += ClothValueNoise(p * 4.0, tile * 4.0) * 0.1;
            return n;
        }

        // ---- Tear field ---------------------------------------------------------------
        // The shroud mesh wraps U without a duplicated seam column, so the last ring of quads
        // interpolates U backwards across the whole range and smears anything keyed off it into
        // a band. Carrying the angle through the rasteriser as a unit vector interpolates
        // cleanly across the wrap, and atan2 puts it back on the far side.
        //
        // UV stays the source rather than object space: a rip is a property of the fabric, and
        // an object-space mapping would let it crawl across the cloth every time the solver
        // swings the garment.
        float2 ClothTearUV(float2 uv, float2 wrapDir)
        {
            if (_TearWrapU < 0.5) return uv;
            float angle = atan2(wrapDir.y, wrapDir.x) * (1.0 / (2.0 * PI)) + 0.5;
            return float2(angle, uv.y);
        }

        // Where the weave is weak. Stretched hard along V because fabric fails along the grain:
        // a failure runs a long way up and down the drape but spreads very little around it,
        // which is what makes damage read as a slit rather than a round bite.
        float ClothTearField(float2 tuv)
        {
            float tile = max(1.0, floor(_TearScale + 0.5));
            float2 p = float2(tuv.x * tile, tuv.y * (tile / max(_TearElongation, 1.0)));
            float n = ClothValueNoise(p, tile) * 0.60;
            n += ClothValueNoise(p * 2.0, tile * 2.0) * 0.28;
            n += ClothValueNoise(p * 4.0, tile * 4.0) * 0.12;
            return n;
        }

        // Loose-fibre field: slower around the garment than down it, so it reads as threads
        // lying horizontally -- the weft pulled out of the vertical rips a hanging drape
        // produces. Two octaves, because real fray has both clumps and single fibres and one
        // frequency alone reads as a machine-cut wave.
        float ClothThreadField(float2 tuv)
        {
            // The U lattice has to stay a whole number of cells or the field stops tiling.
            // Combed only mildly: comb it hard and the boundary steps along in visible
            // horizontal ledges instead of coming apart into threads.
            float tile = max(2.0, floor(_FrayScale / 3.0 + 0.5));
            float2 p = float2(tuv.x * tile, tuv.y * max(_FrayScale, 8.0));
            float n = ClothValueNoise(p, tile) * 0.65;
            n += ClothValueNoise(p * 2.0, tile * 2.0) * 0.35;
            return n;
        }

        // Smooth part of the cut: positive where the weave has given way. Kept separate from the
        // fringe threads so the edge-curl gradient can be differenced off this alone -- finite
        // differences of a field as fine as the threads just return noise.
        float ClothTearBase(float2 tuv)
        {
            // Damage concentrates around the origin height and dies away from there, so rips
            // start where the garment is actually stressed -- the hem, by default -- instead of
            // opening evenly all over at once.
            float weak = saturate(1.0 - abs(tuv.y - _TearOrigin) / max(_TearSpread, 1e-3));
            weak = weak * weak * (3.0 - 2.0 * weak);

            // Pushed past both ends of the field's range: amount 0 stays intact even once the
            // fringe perturbation is added on, amount 1 takes the last threads with it.
            float threshold = _TearAmount * weak * 1.4 - 0.2;
            return threshold - ClothTearField(tuv);
        }

        // Signed distance to the nearest rip in surface units: positive out in intact cloth,
        // negative inside a hole. `gradDir` comes back as a unit vector pointing into the hole.
        //
        // The base field's slope swings wildly with the elongation setting, so measuring the
        // fringe against raw field values gives an edge that is hair-thin in one place and a
        // smear across half the drape in another. Dividing through by the local gradient turns
        // the field into an approximate distance, which is the unit the fray and tension widths
        // are quoted in and the only reason those controls behave consistently.
        float ClothTearDistance(float2 tuv, out float2 gradDir)
        {
            float b0 = ClothTearBase(tuv);

            float e = 0.0035;
            float2 g = float2(ClothTearBase(tuv + float2(e, 0.0)) - b0,
                              ClothTearBase(tuv + float2(0.0, e)) - b0) / e;
            float gl = max(length(g), 1e-3);
            gradDir = g / gl;

            // Fibres reach out past the smooth boundary, so the edge comes apart into threads
            // instead of being cut.
            return -b0 / gl + (ClothThreadField(tuv) - 0.5) * _FrayWidth * 0.015;
        }

        // Shadow and depth passes only need the hole, not the shading around it -- but they
        // have to clip on the same distance the lit pass does, or the garment casts a shadow
        // that does not match the holes in it.
        void ClothClipTear(float2 tuv)
        {
            if (_TearAmount > 0.001)
            {
                float2 gradDir;
                clip(ClothTearDistance(tuv, gradDir));
            }
        }

        // ---- BRDF terms ---------------------------------------------------------------
        // Charlie distribution + Ashikhmin visibility: the standard cloth sheen lobe, which
        // is what gives fabric its bright grazing-angle bloom instead of a hard hot spot.
        float Cloth_D_Charlie(float roughness, float NoH)
        {
            float invR = 1.0 / max(roughness, 1e-3);
            float cos2h = NoH * NoH;
            float sin2h = max(1.0 - cos2h, 0.0078125);
            return (2.0 + invR) * pow(sin2h, invR * 0.5) / (2.0 * PI);
        }

        float Cloth_V_Ashikhmin(float NoV, float NoL)
        {
            return saturate(1.0 / (4.0 * (NoL + NoV - NoL * NoV) + 1e-4));
        }

        // Anisotropic GGX: stretches the highlight along the fold direction, which is the
        // core of the satin/silk look in the reference.
        float Cloth_D_GGX_Aniso(float at, float ab, float ToH, float BoH, float NoH)
        {
            float3 v = float3(ab * ToH, at * BoH, at * ab * NoH);
            float v2 = dot(v, v);
            float a2 = at * ab;
            float w2 = a2 / max(v2, 1e-6);
            return a2 * w2 * w2 * (1.0 / PI);
        }

        float Cloth_V_SmithGGX_Aniso(float at, float ab, float ToV, float BoV, float ToL, float BoL, float NoV, float NoL)
        {
            float lambdaV = NoL * length(float3(at * ToV, ab * BoV, NoV));
            float lambdaL = NoV * length(float3(at * ToL, ab * BoL, NoL));
            return 0.5 / max(lambdaV + lambdaL, 1e-5);
        }

        float3 Cloth_F_Schlick(float3 f0, float VoH)
        {
            float f = pow(1.0 - VoH, 5.0);
            return f0 + (1.0 - f0) * f;
        }

        // Albedo, sheen and specular ride on the surface rather than being read from the
        // material globals, because the fringe of a rip shades as a different fabric to the
        // sheet it was torn out of.
        struct ClothSurface
        {
            float3 N, T, B, V;
            float3 albedo, sheenTint;
            float  sheenScale, specScale;
            float  at, ab, sheenRough;
            float  NoV;
        };

        float3 ShadeClothLight(ClothSurface s, float3 L, float3 lightColor, float atten)
        {
            float3 H = SafeNormalize(L + s.V);
            float NoL = dot(s.N, L);
            float NoH = saturate(dot(s.N, H));
            float VoH = saturate(dot(s.V, H));

            // Wrapped diffuse keeps the shadow terminator soft, the way thick fabric reads.
            float wrapped = saturate((NoL + _DiffuseWrap) / (1.0 + _DiffuseWrap));
            float3 diffuse = s.albedo * wrapped;

            float NoLsat = saturate(NoL);

            float sheen = Cloth_D_Charlie(s.sheenRough, NoH) * Cloth_V_Ashikhmin(s.NoV, NoLsat);
            float3 sheenTerm = s.sheenTint * sheen * s.sheenScale * NoLsat;

            float ToH = dot(s.T, H);
            float BoH = dot(s.B, H);
            float ToV = dot(s.T, s.V);
            float BoV = dot(s.B, s.V);
            float ToL = dot(s.T, L);
            float BoL = dot(s.B, L);

            float Da = Cloth_D_GGX_Aniso(s.at, s.ab, ToH, BoH, NoH);
            float Va = Cloth_V_SmithGGX_Aniso(s.at, s.ab, ToV, BoV, ToL, BoL, s.NoV, NoLsat);
            float3 Fa = Cloth_F_Schlick(float3(0.045, 0.045, 0.045), VoH);
            float3 specTerm = _SpecColor2.rgb * (Da * Va) * Fa * s.specScale * NoLsat;

            return (diffuse + sheenTerm + specTerm) * lightColor * atten;
        }

        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // Once the garment has holes in it the far wall shows through the near one, so both
            // sides have to be drawn. Costs roughly double the fragment work over the garment's
            // screen area; culling back faces instead shows straight through a rip to the sky.
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 tangentWS   : TEXCOORD2;
                float3 bitangentWS : TEXCOORD3;
                float4 uvWrap      : TEXCOORD4; // xy = uv, zw = U as a unit vector (see ClothTearUV)
                float  fogCoord    : TEXCOORD5;
                float3 positionOS  : TEXCOORD6;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nrmInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS = nrmInputs.normalWS;
                OUT.tangentWS = nrmInputs.tangentWS;
                OUT.bitangentWS = nrmInputs.bitangentWS;
                OUT.uvWrap = float4(IN.uv, cos(IN.uv.x * 2.0 * PI), sin(IN.uv.x * 2.0 * PI));
                OUT.positionOS = IN.positionOS.xyz;
                OUT.fogCoord = ComputeFogFactor(posInputs.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float3 N = normalize(IN.normalWS);
                float3 T = normalize(IN.tangentWS);
                float3 B = normalize(IN.bitangentWS);

                // Back faces are the inside of the garment seen through a rip; their normal has
                // to be flipped or the interior lights as though it were still facing out.
                float faceSign = IS_FRONT_VFACE(facing, 1.0, -1.0);
                N *= faceSign;

                float2 tuv = ClothTearUV(IN.uvWrap.xy, IN.uvWrap.zw);

                // ---- Tearing --------------------------------------------------------------
                float fringe = 0.0;   // right at the rip, where the weave has come apart
                float tension = 0.0;  // the band around it that is being pulled taut
                float2 tearGrad = float2(0.0, 0.0);

                if (_TearAmount > 0.001)
                {
                    float dist = ClothTearDistance(tuv, tearGrad);

                    // How far the distance moves per pixel. Taken before the clip -- derivatives
                    // across a quad are garbage once part of it has been discarded -- and folded
                    // into the widths below so the fringe stops aliasing once the garment is far
                    // enough away that it would otherwise be thinner than a pixel.
                    float px = max(fwidth(dist), 1e-6);

                    clip(dist);

                    fringe = 1.0 - smoothstep(0.0, _FrayWidth * 0.020 + px, dist);
                    tension = 1.0 - smoothstep(0.0, _FrayWidth * 0.070 + px * 2.0, dist);
                }

                // ---- Wrinkles -------------------------------------------------------------
                // A closed garment (a tube) has no continuous UV wrap: the last column of quads
                // sweeps U backwards across the whole range, smearing the noise into a seam.
                // Sampling cylindrically from object space sidesteps UVs entirely, and the
                // tiling noise makes the angular wrap continuous.
                float2 wuv = IN.uvWrap.xy;
                if (_WrinkleCylindrical > 0.5)
                {
                    float angle = atan2(IN.positionOS.z, IN.positionOS.x) * (1.0 / (2.0 * PI)) + 0.5;
                    wuv = float2(angle, IN.positionOS.y);
                }

                float wrinkle = ClothWrinkleField(wuv);

                // Perturb the normal by the gradient of the wrinkle field.
                if (_WrinkleStrength > 0.001)
                {
                    float e = 0.004;
                    float wu = ClothWrinkleField(wuv + float2(e, 0));
                    float wv = ClothWrinkleField(wuv + float2(0, e));
                    float2 grad = float2(wu - wrinkle, wv - wrinkle) / e;
                    N = normalize(N - (T * grad.x + B * grad.y) * _WrinkleStrength * 0.02);
                    T = normalize(T - N * dot(N, T));
                    B = normalize(cross(N, T));
                }

                // Roll the surviving weave back at a rip. Freed threads relax and the cloth
                // curls away from the hole; without this the tear reads as a shape punched out
                // of a flat sheet rather than fabric that has come apart.
                if (_TearCurl > 0.001 && tension > 0.001)
                {
                    // tearGrad is a unit vector pointing into the hole, so tilting the normal
                    // against it rolls the surviving cloth back away from the rip.
                    N = normalize(N - (T * tearGrad.x + B * tearGrad.y) * _TearCurl * tension * 0.45);
                    T = normalize(T - N * dot(N, T));
                    B = normalize(cross(N, T));
                }

                float3 V = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                ClothSurface s;
                s.N = N; s.T = T; s.B = B; s.V = V;
                s.NoV = saturate(dot(N, V)) + 1e-4;

                // Loose fibres read far lighter than the sheet they came out of even on a black
                // garment -- they are lit from every side with no bulk behind them to absorb the
                // bounce. That pale fringe is the strongest cue that cloth is torn rather than
                // cleanly cut, so it carries the effect.
                float lint = saturate(fringe * _FrayDensity);
                s.albedo = lerp(_BaseColor.rgb, _FrayColor.rgb, lint);

                // Weave around a rip is pulled taut: it thins out and lightens well before it
                // actually parts.
                s.albedo = lerp(s.albedo, _FrayColor.rgb, tension * _TearTension * 0.35);

                s.sheenTint = lerp(_SheenColor.rgb, _FrayColor.rgb, lint * 0.6);
                s.sheenScale = _SheenIntensity * (1.0 + fringe * _FraySheen);
                s.specScale = _SpecIntensity;

                // Fringe is fuzz, not satin: broaden the sheen lobe and drop the sheet highlight,
                // or the loose threads pick up a mirror streak they could not have.
                s.sheenRough = lerp(_SheenRoughness, 1.0, fringe * 0.7);
                s.specScale *= 1.0 - fringe * 0.8;

                // The reverse of the weave is matte: no satin finish and much less bounce. Seen
                // through a rip it has to read darker than the outside or the garment loses any
                // sense of being a surface with two sides.
                float interior = (faceSign < 0.0) ? (1.0 - _InteriorShade) : 1.0;
                s.albedo *= interior;
                s.sheenScale *= interior;
                s.specScale *= interior * interior;

                float roughness = max(1.0 - _Smoothness, 0.02);
                roughness *= roughness;

                // Vary roughness with the weave so the highlight breaks into scattered soft
                // glints instead of one continuous mirror streak down each fold.
                roughness *= 1.0 + (wrinkle - 0.5) * _RoughnessBreakup * 1.6;

                // Taut fibres lie parallel and polish up, so the highlight tightens where the
                // cloth is being pulled apart.
                roughness *= 1.0 - tension * _TearTension * 0.4;
                roughness = clamp(roughness, 0.002, 1.0);

                float agg = sqrt(1.0 - saturate(abs(_Anisotropy)) * 0.9);
                if (_Anisotropy >= 0)
                {
                    s.at = max(roughness / agg, 1e-3);
                    s.ab = max(roughness * agg, 1e-3);
                }
                else
                {
                    s.at = max(roughness * agg, 1e-3);
                    s.ab = max(roughness / agg, 1e-3);
                }

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float3 color = ShadeClothLight(s, mainLight.direction, mainLight.color,
                                               mainLight.distanceAttenuation * mainLight.shadowAttenuation);

                #ifdef _ADDITIONAL_LIGHTS
                uint lightCount = GetAdditionalLightsCount();
                for (uint li = 0u; li < lightCount; ++li)
                {
                    Light addLight = GetAdditionalLight(li, IN.positionWS, half4(1, 1, 1, 1));
                    color += ShadeClothLight(s, addLight.direction, addLight.color,
                                             addLight.distanceAttenuation * addLight.shadowAttenuation);
                }
                #endif

                // Ambient: fabric picks up a lot of sky bounce at grazing angles, and a loose
                // fibre with nothing behind it picks up more still.
                float3 ambient = SampleSH(N) * _AmbientIntensity;
                color += s.albedo * ambient * (1.0 + fringe * 1.5);
                color += s.sheenTint * ambient * (1.0 - s.NoV) * 0.35 * s.sheenScale;

                float fresnel = pow(1.0 - s.NoV, _FresnelPower);
                color += _FresnelColor.rgb * fresnel * _FresnelIntensity * interior;

                color = MixFog(color, IN.fogCoord);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            // Cast shadows from back faces: on thin, closed garments front-face casting puts
            // the depth surface flush against the lit side and stipples it with shadow acne.
            Cull Front

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma target 3.0

            #pragma multi_compile_instancing
            float3 _LightDirection;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionHCS : SV_POSITION;
                float4 uvWrap      : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ShadowVaryings ShadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                positionWS = ApplyShadowBias(positionWS, normalWS, _LightDirection);
                OUT.positionHCS = TransformWorldToHClip(positionWS);
                OUT.uvWrap = float4(IN.uv, cos(IN.uv.x * 2.0 * PI), sin(IN.uv.x * 2.0 * PI));
                return OUT;
            }

            half4 ShadowFrag(ShadowVaryings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                ClothClipTear(ClothTearUV(IN.uvWrap.xy, IN.uvWrap.zw));
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            // Matches the forward pass, so anything reading the depth buffer sees the same
            // two-sided garment the lit pass drew.
            Cull Off

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma target 3.0

            #pragma multi_compile_instancing
            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionHCS : SV_POSITION;
                float4 uvWrap      : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert(DepthAttributes IN)
            {
                DepthVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uvWrap = float4(IN.uv, cos(IN.uv.x * 2.0 * PI), sin(IN.uv.x * 2.0 * PI));
                return OUT;
            }

            half4 DepthFrag(DepthVaryings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                ClothClipTear(ClothTearUV(IN.uvWrap.xy, IN.uvWrap.zw));
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
