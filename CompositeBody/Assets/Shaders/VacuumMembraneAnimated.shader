Shader "CompositeBody/VacuumMembraneAnimated"
{
    // The moving version of CompositeBody/VacuumMembrane. Same sheet, same contact model, same
    // crumple -- everything down to the shading is unchanged, and a material ported across by
    // copying its values looks identical with the motion turned down to zero.
    //
    // The whole idea is in one line of the vertex shader: the sheet can only move where it is
    // not touching anything. Film pulled onto a surface is pinned by the vacuum behind it and
    // cannot go anywhere; film spanning a gap is the only part with slack in it. So every motion
    // here is multiplied by that slack, read straight off the gap channel the builder baked. The
    // result is that the taut parts stay nailed to the object and the webs between them breathe,
    // which is what the motion needs to look like to read as a film rather than as a wobbling
    // mesh.
    //
    // Three channels, which do different jobs:
    // - Breath: slow, large-scale swell. Carries the sense of something behind the sheet.
    // - Ripple: a fast travelling wave, far finer. Without it the breath reads as the whole
    //   object scaling up and down rather than as a surface moving.
    // - Crease drift: the crumple field slides along its own ridges. This is the one that
    //   animates the shading rather than the silhouette, and it does most of the visible work.
    //
    // Kept as a separate file rather than a keyword on the original, so that the still look --
    // which the avatar is already shipping -- cannot be changed by anything done in here. The
    // cost is that the two now have to be tuned in step; see the note in the project.
    Properties
    {
        [Header(Film)]
        _FilmColor ("Film Tint (in contact)", Color) = (0.72, 0.78, 0.86, 1)
        _FrostColor ("Frost Colour (spanning air)", Color) = (0.88, 0.91, 0.96, 1)
        _ClearAlpha ("Clear Alpha", Range(0, 1)) = 0.10
        _FrostAlpha ("Frost Alpha", Range(0, 1)) = 0.66

        [Header(Contact)]
        _ContactFloor ("Contact Floor (metres)", Range(0, 0.05)) = 0.007
        _ContactRange ("Contact Range (metres)", Range(0.002, 0.30)) = 0.020
        _ContactSharpness ("Contact Falloff", Range(0.2, 4)) = 1.3
        _TautClarity ("Taut Clarity", Range(0, 2)) = 0.6

        [Header(Crumple)]
        _CreaseScale ("Crease Scale", Float) = 22
        _CreaseStretch ("Crease Stretch", Range(1, 16)) = 6
        _CreaseStrength ("Crease Strength", Range(0, 1)) = 0.55
        _CreaseSharpness ("Crease Sharpness", Range(1, 8)) = 3.2

        [Header(Motion)]
        _BreathAmount ("Breath Depth (metres)", Range(0, 0.08)) = 0.016
        _BreathSpeed ("Breath Speed", Range(0, 2)) = 0.22
        _BreathScale ("Breath Spatial Scale", Range(0.1, 8)) = 1.4
        _BreathNormal ("Breath Normal Exaggeration", Range(0, 60)) = 20
        _RippleAmount ("Ripple Depth (metres)", Range(0, 0.03)) = 0.0035
        _RippleSpeed ("Ripple Speed", Range(0, 6)) = 1.1
        _RippleScale ("Ripple Scale", Range(1, 40)) = 13
        _CreaseDrift ("Crease Drift", Range(0, 2)) = 0.35
        _SlackPower ("Slack Response", Range(0.25, 4)) = 1.0
        _ManualTime ("Manual Time (negative uses real time)", Float) = -1

        [Header(Reveal)]
        _Reveal ("Reveal", Range(0, 1)) = 1
        _GrowOrigin ("Grow Origin (object space)", Vector) = (0, 0, 0, 0)
        _GrowRadius ("Grow Radius (metres, 0 fades everywhere at once)", Float) = 0
        _GrowSoftness ("Grow Edge Softness", Range(0.01, 1)) = 0.25
        _GrowInflate ("Grow Inflate", Range(0, 1)) = 1
        _GrowEdgeFrost ("Growing Edge Frost", Range(0, 2)) = 1

        [Header(Sheen)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.93
        _SpecColor2 ("Specular Colour", Color) = (1, 1, 1, 1)
        _SpecIntensity ("Specular Intensity", Range(0, 12)) = 4.0
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 2.6
        _FresnelIntensity ("Fresnel Intensity", Range(0, 4)) = 1.0
        _EdgeOpacity ("Edge Opacity", Range(0, 2)) = 0.55

        [Header(Light Through)]
        _Translucency ("Translucency", Range(0, 4)) = 1.1
        _TranslucencyPower ("Translucency Power", Range(1, 16)) = 4.0
        _AmbientIntensity ("Ambient Intensity", Range(0, 2)) = 0.55

        [Header(Debug)]
        [Toggle] _ShowContact ("Visualise Contact (red on skin blue spanning air)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CompositeFog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _FilmColor;
                float4 _FrostColor;
                float _ClearAlpha;
                float _FrostAlpha;
                float _ContactFloor;
                float _ContactRange;
                float _ContactSharpness;
                float _TautClarity;
                float _CreaseScale;
                float _CreaseStretch;
                float _CreaseStrength;
                float _CreaseSharpness;
                float _BreathAmount;
                float _BreathSpeed;
                float _BreathScale;
                float _BreathNormal;
                float _RippleAmount;
                float _RippleSpeed;
                float _RippleScale;
                float _CreaseDrift;
                float _SlackPower;
                float _ManualTime;
                float _Reveal;
                float4 _GrowOrigin;
                float _GrowRadius;
                float _GrowSoftness;
                float _GrowInflate;
                float _GrowEdgeFrost;
                float _Smoothness;
                float4 _SpecColor2;
                float _SpecIntensity;
                float _FresnelPower;
                float _FresnelIntensity;
                float _EdgeOpacity;
                float _Translucency;
                float _TranslucencyPower;
                float _AmbientIntensity;
                float _ShowContact;
            CBUFFER_END

            // ---- Crumple ------------------------------------------------------------------
            float MembraneHash(float3 p)
            {
                return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453123);
            }

            float MembraneNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = MembraneHash(i + float3(0, 0, 0));
                float n100 = MembraneHash(i + float3(1, 0, 0));
                float n010 = MembraneHash(i + float3(0, 1, 0));
                float n110 = MembraneHash(i + float3(1, 1, 0));
                float n001 = MembraneHash(i + float3(0, 0, 1));
                float n101 = MembraneHash(i + float3(1, 0, 1));
                float n011 = MembraneHash(i + float3(0, 1, 1));
                float n111 = MembraneHash(i + float3(1, 1, 1));

                float x00 = lerp(n000, n100, f.x);
                float x10 = lerp(n010, n110, f.x);
                float x01 = lerp(n001, n101, f.x);
                float x11 = lerp(n011, n111, f.x);
                return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
            }

            float MembraneCreaseField(float3 p)
            {
                float3 q = float3(p.x, p.y / max(_CreaseStretch, 1.0), p.z);

                float n = MembraneNoise(q);
                n = 1.0 - abs(n * 2.0 - 1.0);
                n = pow(saturate(n), _CreaseSharpness);

                float n2 = MembraneNoise(q * 2.7 + 11.3);
                n2 = 1.0 - abs(n2 * 2.0 - 1.0);
                n += pow(saturate(n2), _CreaseSharpness) * 0.45;

                float n3 = MembraneNoise(q * 6.1 + 37.7);
                n3 = 1.0 - abs(n3 * 2.0 - 1.0);
                n += pow(saturate(n3), _CreaseSharpness) * 0.22;

                return n;
            }

            // ---- Motion -------------------------------------------------------------------

            // Real time normally. A non-negative _ManualTime replaces it, which is the only way
            // to capture a moving sheet from a batch-mode still render -- _Time does not advance
            // when the editor renders a camera to a file, so without this every frame of a
            // preview sweep would come out identical.
            float MembraneTime()
            {
                return (_ManualTime >= 0.0) ? _ManualTime : _Time.y;
            }

            // How free this part of the sheet is to move: zero where the film is against a
            // surface, one where it is spanning air. Everything in here is multiplied by it.
            float MembraneSlack(float gap)
            {
                float s = saturate((gap - _ContactFloor) / max(_ContactRange, 1e-4));
                return pow(s, _SlackPower);
            }

            // ---- Reveal -------------------------------------------------------------------

            // When this part of the sheet forms, as 0 at the growth origin rising to 1 at the
            // far end of the growth radius. A radius of zero means no spatial order at all and
            // the whole sheet fades together -- which is the plain fade in and out, from the
            // same parameter, rather than a second mode to maintain.
            float MembraneBirth(float3 restPos)
            {
                if (_GrowRadius <= 1e-4) return 0.0;
                return saturate(distance(restPos, _GrowOrigin.xyz) / _GrowRadius);
            }

            // How far along this part is, 0 unformed to 1 finished.
            //
            // The front is pushed past both ends by the softness band on purpose: without it,
            // _Reveal = 0 would already have the origin half formed, and _Reveal = 1 would leave
            // the furthest point still mid-transition. Those two have to be exactly empty and
            // exactly whole, or a beat that fades the room out does not finish fading it out.
            float MembraneLocalReveal(float born)
            {
                float soft = max(_GrowSoftness, 1e-3);
                float front = _Reveal * (1.0 + soft);
                return saturate((front - born) / soft);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 filmData   : TEXCOORD1;  // x = gap between film and body, from the builder
                float3 restPos    : TEXCOORD2;  // the film's own bind pose, anchors the creases
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 restOS      : TEXCOORD2;
                float3 restWS      : TEXCOORD3;
                float  gap         : TEXCOORD4;
                float  fogCoord    : TEXCOORD5;
                float  reveal      : TEXCOORD6;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float t = MembraneTime();
                float3 nOS = normalize(IN.normalOS);
                float slack = MembraneSlack(IN.filmData.x);

                // Sampled in the film's own rest space, and drifting along one axis of the noise
                // rather than through it in all three: the swell stays attached to the sheet and
                // rolls along it, instead of boiling in place the way a plain time offset gives.
                float3 bp = IN.restPos * _BreathScale + float3(0.0, t * _BreathSpeed, 0.0);
                float breathRaw = MembraneNoise(bp);
                float breath = breathRaw * 2.0 - 1.0;

                float ripple = sin(dot(IN.restPos, float3(0.6, 1.0, 0.35)) * _RippleScale - t * _RippleSpeed);

                float local = MembraneLocalReveal(MembraneBirth(IN.restPos));

                // Unformed film does not breathe. The motion arrives with the sheet rather than
                // playing out on something that is not there yet.
                float push = (breath * _BreathAmount + ripple * _RippleAmount) * slack * local;

                // Growing out, rather than fading in on top of what is already there. The gap is
                // how far the sheet stands off its object and the normal is the way back, so
                // pulling an unformed vertex back along it by its own gap lays it exactly on the
                // surface -- and the sheet then lifts off as it forms. A film that merely faded
                // up at full offset would read as a ghost appearing in mid air.
                float sink = IN.filmData.x * _GrowInflate * (1.0 - local);

                // The normal has to follow, or the breath moves the silhouette and the shading
                // never notices -- which reads as the mesh sliding rather than the sheet filling.
                // The slope of the swell projected onto the surface, not an exact derivative of
                // the displaced position: close enough at these amplitudes, and it avoids needing
                // a tangent frame the rest of this shader deliberately does not have.
                //
                // Scaled by the breath's own amplitude and spatial scale, so it is the slope of
                // the displacement actually applied rather than a free-floating wobble. That
                // coupling is what makes _BreathAmount = 0 mean no motion at all: without it, a
                // material with every amplitude zeroed still had its normals shaken about, and
                // "the still look with the motion turned off" was not true. _BreathNormal is an
                // exaggeration factor over that true slope, which is far too subtle to read on
                // its own at these amplitudes.
                const float e = 0.08;
                float3 bg = float3(MembraneNoise(bp + float3(e, 0, 0)) - breathRaw,
                                   MembraneNoise(bp + float3(0, e, 0)) - breathRaw,
                                   MembraneNoise(bp + float3(0, 0, e)) - breathRaw) / e;
                bg = bg - nOS * dot(bg, nOS);
                float slope = 2.0 * _BreathScale * _BreathAmount * _BreathNormal * slack * local;
                float3 movedN = normalize(nOS - bg * slope);

                float3 positionOS = IN.positionOS.xyz + nOS * (push - sink);

                VertexPositionInputs posInputs = GetVertexPositionInputs(positionOS);

                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(movedN);
                OUT.restOS = IN.restPos;
                OUT.restWS = TransformObjectToWorld(IN.restPos);

                // Film pushed outward has more air behind it than the bake measured, so it
                // frosts a little further. Only outward: a sheet sucked back toward the surface
                // is not in contact with it, it is merely closer. Sinking back as it forms does
                // count though -- that really is the sheet lying on the object.
                OUT.gap = max(IN.filmData.x - sink, 0.0) + max(push, 0.0);
                OUT.reveal = local;

                OUT.fogCoord = ComputeFogFactor(posInputs.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float3 N = normalize(IN.normalWS) * IS_FRONT_VFACE(facing, 1.0, -1.0);
                float3 V = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                float t = MembraneTime();

                // ---- Crumple ---------------------------------------------------------------
                // Drifting mostly along the stretch axis, so the ridges slide along their own
                // length. Drifting across them instead makes the whole crumple pattern scroll
                // sideways over the object, which reads as a moving texture rather than as a
                // sheet shifting against what it is wrapped around.
                float3 cp = IN.restOS * _CreaseScale + t * _CreaseDrift * float3(0.11, 1.0, 0.19);
                float c0 = MembraneCreaseField(cp);
                float e = 0.06;
                float3 cg = float3(MembraneCreaseField(cp + float3(e, 0, 0)) - c0,
                                   MembraneCreaseField(cp + float3(0, e, 0)) - c0,
                                   MembraneCreaseField(cp + float3(0, 0, e)) - c0) / e;
                cg = cg - N * dot(cg, N);
                N = normalize(N - cg * _CreaseStrength * 0.12);

                // ---- Contact ---------------------------------------------------------------
                float contact = 1.0 - saturate((IN.gap - _ContactFloor) / max(_ContactRange, 1e-4));
                contact = pow(contact, _ContactSharpness);

                // With the sheet moving, this is doing real work: the area Jacobian compares the
                // displaced surface against the bake, so film stretched thin by the swell clears
                // and film gathered up by it goes milky, on its own and for free.
                float3 dPx = ddx(IN.positionWS), dPy = ddy(IN.positionWS);
                float3 dRx = ddx(IN.restWS),     dRy = ddy(IN.restWS);
                float areaRest = length(cross(dRx, dRy));
                float areaRatio = (areaRest > 1e-12) ? length(cross(dPx, dPy)) / areaRest : 1.0;
                float taut = saturate((clamp(areaRatio, 0.25, 4.0) - 1.0) * 2.0);
                contact = saturate(contact + taut * _TautClarity);

                float frost = 1.0 - contact;

                // The advancing front goes milky. A sheet still being drawn off a surface has
                // the most air behind it exactly while it is moving, and it is that bright band
                // running ahead of the finished film that makes the growth read as growth rather
                // than as opacity being turned up.
                float reveal = saturate(IN.reveal);
                float growEdge = saturate(reveal * (1.0 - reveal) * 4.0);
                frost = saturate(frost + growEdge * _GrowEdgeFrost);

                if (_ShowContact > 0.5)
                {
                    float3 dbg = lerp(float3(0.15, 0.35, 0.95), float3(0.95, 0.25, 0.15), contact);
                    return half4(dbg, 1.0);
                }

                // ---- Shading ---------------------------------------------------------------
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float atten = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                float3 L = mainLight.direction;

                float NoV = saturate(dot(N, V)) + 1e-4;
                float NoL = dot(N, L);

                float3 film = lerp(_FilmColor.rgb, _FrostColor.rgb, frost);

                float fresnel = pow(1.0 - NoV, _FresnelPower);

                float alpha = lerp(_ClearAlpha, _FrostAlpha, frost);
                alpha = saturate(alpha + fresnel * _EdgeOpacity);
                alpha *= reveal;

                float wrapped = saturate((NoL + 0.55) / 1.55);
                float3 color = film * wrapped * mainLight.color * atten * (0.35 + frost * 0.65);
                color += film * SampleSH(N) * _AmbientIntensity;

                float through = pow(saturate(dot(-L, V)), _TranslucencyPower);
                color += _FrostColor.rgb * through * _Translucency * atten * (0.25 + frost * 0.75);

                float roughness = max(1.0 - _Smoothness, 0.015);
                roughness *= roughness;

                float3 H = SafeNormalize(L + V);
                float NoH = saturate(dot(N, H));
                float VoH = saturate(dot(V, H));
                float a2 = roughness * roughness;
                float dDen = NoH * NoH * (a2 - 1.0) + 1.0;
                float D = a2 / max(PI * dDen * dDen, 1e-7);
                float Vis = 0.5 / max(NoV + saturate(NoL), 1e-4);
                float3 F = _SpecColor2.rgb + (1.0 - _SpecColor2.rgb) * pow(1.0 - VoH, 5.0);
                float3 spec = D * Vis * F * _SpecIntensity * saturate(NoL) * mainLight.color * atten;

                // The highlights are added at full strength rather than through alpha, so they
                // are the one thing the reveal has to scale by hand -- left alone, an unformed
                // sheet would still glint.
                float3 outRGB = color * alpha;
                outRGB += spec * reveal;
                outRGB += _SpecColor2.rgb * fresnel * _FresnelIntensity * alpha;

                outRGB = CompositeFogMix(outRGB, IN.positionWS, IN.fogCoord);
                return half4(outRGB, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
