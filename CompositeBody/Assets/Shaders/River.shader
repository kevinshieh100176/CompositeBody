Shader "CompositeBody/River"
{
    // A river surface: opaque, flowing along its spline, lit almost entirely by specular.
    //
    // WHY OPAQUE. Nothing here reads the colour buffer, so the water can sit in the opaque queue
    // and that is a real win rather than a limitation: it writes depth, so everything drawn after
    // it sorts correctly against it; it gets URP-Performant's MSAA 4 on the shoreline edges for
    // free; and it never fights the membranes and the 光圈 for transparent sort order. A
    // refractive river would need _CameraOpaqueTexture, which is a fullscreen colour copy per eye.
    //
    // WHERE THE LOOK COMES FROM. There is no sky and no horizon in this piece, so normal-map
    // detail has nothing to mirror and a reflection probe would return black. What sells the water
    // is SPECULAR: sharp glints of the purple and yellow orbit lights sliding across a moving
    // surface. URP-Performant runs Forward with additional lights per-pixel and a limit of 8, so
    // both orbits reach the water through the classic light loop below. HDR is on, so a glint can
    // exceed 1 and bloom.
    //
    // WHY V IS METRES. BuildRiver bakes distance along the spline into UV.y, so everything that
    // scrolls here travels down the channel and round the bends instead of in a fixed world
    // direction. Scrolling is driven by _RiverTime off the shared clock, never _Time, because two
    // players stand in the same room and must see the same waves.

    Properties
    {
        [Header(Colour)]
        _DeepColor ("Deep", Color) = (0.016, 0.047, 0.063, 1)
        _ShallowColor ("Grazing", Color) = (0.09, 0.16, 0.19, 1)
        _FoamColor ("Foam", Color) = (0.72, 0.78, 0.80, 1)

        [Header(Surface)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.88
        _SpecIntensity ("Specular Intensity", Range(0, 40)) = 6
        _DiffuseWrap ("Diffuse Wrap", Range(0, 1)) = 0.35

        // Water is not a diffuse surface. The colour you see in real water is light scattered out
        // of its depth, not a lambert response, so the diffuse term here is a cheap stand-in and
        // should stay quiet -- left at full strength it turns the surface into painted plastic.
        _DiffuseIntensity ("Diffuse Intensity", Range(0, 1)) = 0.3

        // 0.02 is water's actual reflectance head-on (IOR 1.33). Exposed rather than hard-coded
        // only so it can be pushed for effect; physically it does not vary.
        _Fresnel0 ("Reflectance Head-On", Range(0, 0.2)) = 0.02
        _FresnelStrength ("Fresnel Strength", Range(0, 1)) = 1

        [Header(Depth)]
        // Per metre. Higher means the water reaches its full colour in a shallower channel --
        // muddy river water rather than clear.
        _DepthFalloff ("Extinction (per m)", Range(0.05, 4)) = 0.5
        _ShallowScatter ("Shallow Scatter Floor", Range(0, 1)) = 0.12
        _WaveDepthScale ("Full Waves At Depth (m)", Range(0.05, 3)) = 0.8

        [Header(Distance)]
        _NormalFadeStart ("Normal Fade Start (m)", Range(0, 200)) = 8
        _NormalFadeRange ("Normal Fade Range (m)", Range(1, 400)) = 55
        _NormalFadeFloor ("Distant Normal Strength", Range(0, 1)) = 0.12

        [Header(Flow)]
        _FlowSpeed ("Flow Speed", Range(0, 8)) = 1.0

        [Header(Waves)]
        _WaveAmplitude ("Amplitude (m)", Range(0, 0.5)) = 0.035
        _WaveFreqA ("Frequency A", Range(0, 12)) = 1.6
        _WaveFreqB ("Frequency B", Range(0, 12)) = 4.3
        _WaveFreqC ("Cross Frequency", Range(0, 12)) = 2.1
        _WaveFadeStart ("Wave Fade Start (m)", Range(0, 200)) = 12
        _WaveFadeRange ("Wave Fade Range (m)", Range(1, 200)) = 25

        [Header(Normal Detail)]
        [Normal] _NormalA ("Normal A", 2D) = "bump" {}
        [Normal] _NormalB ("Normal B", 2D) = "bump" {}
        _NormalSpeedA ("Scroll A", Range(0, 4)) = 1.0
        _NormalSpeedB ("Scroll B", Range(0, 4)) = 0.55
        _NormalStrength ("Strength", Range(0, 3)) = 1.0

        [Header(Foam)]
        _FoamTex ("Foam Mask", 2D) = "white" {}
        _FoamScale ("Foam Scale", Range(0.01, 4)) = 0.5
        _FoamSpeed ("Foam Scroll", Range(0, 4)) = 0.8
        _FoamCoverage ("Foam Coverage", Range(0, 4)) = 2.0
        _FoamSoftness ("Foam Softness", Range(0.001, 0.5)) = 0.12
        _EdgeFoam ("Bank Foam Width", Range(0, 0.5)) = 0.12
        _EdgeFoamSoftness ("Bank Foam Softness", Range(0, 1)) = 0.7

        [Header(Reveal)]
        _Reveal ("Reveal", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        // --- forward -------------------------------------------------------------------------
        Pass
        {
            Name "RiverForward"
            Tags { "LightMode" = "UniversalForward" }

            // PREMULTIPLIED ALPHA, not the usual SrcAlpha blend. Transmission and reflection are
            // different things: what the water lets through scales with how clear it is, but a
            // glint off the surface does not get fainter because the water is shallow. Straight
            // alpha blending would multiply the specular by alpha and erase the highlights
            // exactly at the margins, where the surface is most visible.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _ADDITIONAL_LIGHTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "River.hlsl"
            #include "CompositeFog.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                float4 profile    : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float4 profile     : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
                float3 normalWS    : TEXCOORD3;
                float3 tangentWS   : TEXCOORD4;
                float3 bitangentWS : TEXCOORD5;
                float  fogCoord    : TEXCOORD6;
                float4 screenPos   : TEXCOORD7;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 displacedOS =
                    RiverDisplace(IN.positionOS.xyz, IN.normalOS, IN.uv, IN.profile);

                VertexPositionInputs posInputs = GetVertexPositionInputs(displacedOS);
                VertexNormalInputs normalInputs =
                    GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS = normalInputs.normalWS;
                OUT.tangentWS = normalInputs.tangentWS;
                OUT.bitangentWS = normalInputs.bitangentWS;
                OUT.uv = IN.uv;
                OUT.profile = IN.profile;
                OUT.fogCoord = ComputeFogFactor(posInputs.positionCS.z);
                OUT.screenPos = ComputeScreenPos(posInputs.positionCS);
                return OUT;
            }

            /// Normalised Blinn-Phong, written out rather than taken from a URP helper so the
            /// glint shape stays under this shader's control.
            half3 ShadeLight(half3 albedo, half foam, half scatter, half3 N, half3 V, half3 L,
                             half3 lightColor)
            {
                // Wrapped, because foam scatters and because a hard terminator on water reads as
                // plastic. The wrap is the same for both so the surface stays coherent.
                half ndl = dot(N, L);
                half diffuse = saturate((ndl + _DiffuseWrap) / (1.0 + _DiffuseWrap));

                half3 H = SafeNormalize(L + V);
                half nh = saturate(dot(N, H));
                half exponent = exp2(_Smoothness * 11.0) + 2.0;
                half spec = pow(nh, exponent) * (exponent + 8.0) * 0.0397887;

                // FRESNEL. Water reflects about 2% of what hits it head-on and nearly all of it
                // at a grazing angle -- a ratio of roughly fifty to one. That single curve is why
                // real water is almost black at your feet and a bright sheet toward the horizon,
                // and a uniform highlight is the main reason water reads as plastic without it.
                //
                // Schlick against the HALF VECTOR, not the surface normal: this is the
                // reflectance of the microfacets that are actually mirroring this light toward
                // the eye, which is a different question from how reflective the surface looks
                // overall.
                half voh = saturate(dot(V, H));
                half fresnel = _Fresnel0 + (1.0 - _Fresnel0) * pow(1.0 - voh, 5.0);
                half reflectance = lerp(1.0, fresnel, _FresnelStrength);

                // Foam is diffuse, so it should not also carry a sharp highlight.
                half specMask = 1.0 - foam * 0.85;

                return lightColor *
                       (albedo * diffuse * _DiffuseIntensity * scatter +
                        spec * reflectance * _SpecIntensity * specMask);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float flow = max(IN.profile.x, 0.0);
                float scroll = _RiverTime * _FlowSpeed * flow;

                // Both layers scroll in V, which is metres down the channel. The _ST y value is
                // therefore "tiles per metre", which is why these numbers are small.
                float2 uvA = IN.uv * _NormalA_ST.xy + _NormalA_ST.zw;
                float2 uvB = IN.uv * _NormalB_ST.xy + _NormalB_ST.zw;
                uvA.y -= scroll * _NormalSpeedA;
                uvB.y -= scroll * _NormalSpeedB;

                half3 nA = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_NormalA, sampler_NormalA, uvA), _NormalStrength);
                half3 nB = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_NormalB, sampler_NormalB, uvB), _NormalStrength);

                // DISTANCE FLATTENING. Real water averages out with distance and goes glassy;
                // holding full normal detail out to the horizon is both wrong and an artifact
                // generator. Once the ripples fall below a pixel they alias into shimmer, and in
                // stereo the two eyes shimmer differently, which is far more unpleasant in a
                // headset than it ever is on a screen. Flattening the normals also lets the
                // Fresnel term above do its job: smooth distant water turns into the bright sheet
                // that reads as a river running away from you.
                float eyeDistance = distance(IN.positionWS, _WorldSpaceCameraPos);
                half flatten = saturate((eyeDistance - _NormalFadeStart) /
                                        max(_NormalFadeRange, 0.01));
                half detail = lerp(1.0, _NormalFadeFloor, flatten);

                // Two layers at different scales and speeds, so neither one's tiling is legible.
                half3 normalTS = normalize(half3((nA.xy + nB.xy) * detail, nA.z * nB.z));

                half3x3 tangentToWorld =
                    half3x3(IN.tangentWS, IN.bitangentWS, IN.normalWS);
                half3 N = normalize(mul(normalTS, tangentToWorld));
                half3 V = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                // --- foam -------------------------------------------------------------------
                float2 foamUV = IN.uv * _FoamScale;
                foamUV.y -= scroll * _FoamSpeed;
                half foamTex = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, foamUV).r;

                // The baked amount moves the CUT POINT rather than scaling the sample. Scaling it
                // meant the mask could never reach a fixed threshold at low foam settings -- with
                // the default curve at 0.15 the highest possible value was 0.30 against a
                // threshold of 0.55, so surface foam was mathematically unreachable. Here
                // coverage 0.3 means "the brightest 30% of the mask is foam", which is both
                // reachable and what the word means.
                half coverage = saturate(IN.profile.y * _FoamCoverage);
                half cut = 1.0 - coverage;
                half surfaceFoam = smoothstep(cut - _FoamSoftness, cut + _FoamSoftness, foamTex);

                // Foam gathered at the banks. This is what softens the waterline: the river is
                // opaque, so there is no alpha edge to fade, and the depth texture cannot help an
                // opaque surface -- see the note in River.hlsl.
                half toBank = min(IN.uv.x, 1.0 - IN.uv.x);
                half bandInner = _EdgeFoam * (1.0 - _EdgeFoamSoftness);
                half bank = 1.0 - smoothstep(bandInner, max(_EdgeFoam, 1e-4), toBank);

                // Bank foam is modulated by the mask rather than gated by it: the mask is mostly
                // dark by design, and multiplying straight through left the waterline bare, which
                // is the one place foam has a job to do.
                half foam = saturate(max(surfaceFoam, bank * lerp(0.35, 1.0, foamTex)));

                // --- colour -----------------------------------------------------------------
                // DEPTH, MEASURED. The thickness of water between this fragment and the bed
                // behind it, read from the depth texture. This works now and did not before for
                // one reason: the river is transparent, so it is not itself in that texture, and
                // there is a bed in it to find.
                float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 1e-6);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfaceEye = LinearEyeDepth(IN.positionHCS.z, _ZBufferParams);

                // Eye depth is measured along the view axis, but light travels along the view
                // RAY, and the two differ by the angle off centre. Converting makes the depth
                // honest toward the edges of a wide field of view -- the same correction the
                // 光圈 cones and the orbit glows make against this texture.
                float toSurface = distance(IN.positionWS, _WorldSpaceCameraPos);
                float throughWater = max(sceneEye - surfaceEye, 0.0) *
                                     (toSurface / max(surfaceEye, 1e-5));

                // Beer-Lambert: what gets back out falls off exponentially with the path. Here
                // the path is travelled once, not twice, because this is the distance the view
                // ray actually crosses rather than a plumb depth.
                half column = 1.0 - exp(-throughWater * _DepthFalloff);

                half3 albedo = lerp(_ShallowColor.rgb, _DeepColor.rgb, column);
                albedo = lerp(albedo, _FoamColor.rgb, foam);

                // The diffuse term here stands in for that subsurface scatter, so it has to scale
                // with the column too -- a centimetre of water over nothing cannot send much
                // back. The floor keeps the margins from going absolutely black, which reads as
                // a hole rather than as shallows.
                half scatter = lerp(_ShallowScatter, 1.0, column);

                half3 color = albedo * SampleSH(N) * scatter;

                Light mainLight = GetMainLight();
                color += ShadeLight(albedo, foam, scatter, N, V, mainLight.direction,
                                    mainLight.color * mainLight.distanceAttenuation);

                #ifdef _ADDITIONAL_LIGHTS
                    uint count = GetAdditionalLightsCount();
                    for (uint i = 0u; i < count; i++)
                    {
                        Light light = GetAdditionalLight(i, IN.positionWS);
                        color += ShadeLight(albedo, foam, scatter, N, V, light.direction,
                                            light.color * light.distanceAttenuation);
                    }
                #endif

                // Same air as everything else in the piece. This is what gives the far stretch of
                // the river its distance.
                color = CompositeFogAttenuate(color, IN.positionWS, IN.fogCoord);

                // PREMULTIPLIED OUT. The colour above is already scaled by the column through
                // `scatter`, so it is emitted as-is and alpha only says how much of the bed this
                // water hides. Shallow margins therefore let the bed show through and deep
                // channel does not -- and because the blend is One OneMinusSrcAlpha, the
                // specular that is folded into `color` survives at the edges instead of being
                // multiplied away with the alpha.
                //
                // Foam is opaque whatever the depth: it is bubbles sitting on the surface, not
                // water you can see into.
                half alpha = saturate(max(column, foam));

                return half4(color * _Reveal, alpha * _Reveal);
            }
            ENDHLSL
        }

        // --- depth ---------------------------------------------------------------------------
        // Required, not optional. URP-Performant sets m_RequireDepthTexture, so a depth prepass
        // runs; without this pass the river would be absent from the depth texture that the 光圈
        // cones and the orbit glows read, and they would draw straight through it.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "River.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 profile    : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 displacedOS =
                    RiverDisplace(IN.positionOS.xyz, IN.normalOS, IN.uv, IN.profile);
                OUT.positionHCS = TransformObjectToHClip(displacedOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                return 0;
            }
            ENDHLSL
        }

        // --- depth normals -------------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
            #include "River.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 profile    : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 displacedOS =
                    RiverDisplace(IN.positionOS.xyz, IN.normalOS, IN.uv, IN.profile);

                OUT.positionHCS = TransformObjectToHClip(displacedOS);

                // The geometric normal, not the normal-mapped one: this buffer is read for
                // depth-normals effects, and the wave detail belongs in the forward pass.
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                return half4(normalize(IN.normalWS) * 0.5 + 0.5, 0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
