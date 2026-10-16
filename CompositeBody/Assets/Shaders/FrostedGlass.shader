Shader "CompositeBody/FrostedGlass"
{
    Properties
    {
        [Header(Glass)]
        _TintColor ("Tint", Color) = (0.85, 0.93, 1.0, 1)
        _TintStrength ("Tint Strength", Range(0, 1)) = 0.25
        _Opacity ("Opacity (milkiness)", Range(0, 1)) = 0.15
        // How much of the sharp scene behind the glass is replaced by the frosted layer. Water and
        // other transparents are missing from the blurred backdrop, so below 1 they show through.
        _Coverage ("Frost Coverage", Range(0, 1)) = 0.5

        [Header(Frost)]
        _BlurRadius ("Blur Radius", Range(0, 0.05)) = 0.012
        _FrostNoiseScale ("Frost Grain Scale", Range(10, 2000)) = 500
        _FrostDistortion ("Frost Distortion", Range(0, 0.02)) = 0.004

        [Header(Condensation)]
        _FogColor ("Fog Color", Color) = (1.0, 0.5, 0.35, 1)
        _Condensation ("Condensation Amount", Range(0, 1)) = 1
        // Pattern sizes are in world units (per metre) so they look the same on any pane.
        _StreakDensity ("Drip Streaks per Metre", Range(1, 60)) = 14
        _StreakLength ("Drip Length (1/m)", Range(0.1, 6)) = 0.9
        _StreakClarity ("Drip Clear Amount", Range(0, 1)) = 0.85
        _DropScaleSmall ("Small Droplets per Metre", Range(5, 200)) = 70
        _DropScaleLarge ("Large Droplets per Metre", Range(2, 80)) = 22
        _DropRefraction ("Droplet Refraction", Range(0, 0.1)) = 0.03
        _Sparkle ("Droplet Sparkle", Range(0, 8)) = 2.5

        [Header(Finger Doodles)]
        // Hearts, arrows, "xo" and sparkles wiped through the fog with a fingertip.
        _DoodleAmount ("Doodle Amount (share of cells with a doodle)", Range(0, 1)) = 0.4
        _DoodleSize ("Doodle Cell Size (m)", Range(0.15, 1.5)) = 0.45
        _DoodleWidth ("Finger Width (m)", Range(0.004, 0.04)) = 0.016
        _DoodleClarity ("Doodle Clear Amount", Range(0, 1)) = 1

        [Header(Area Variation)]
        // Large soft patches where the frost thins out or disappears, so the pane is never evenly fogged.
        _AreaStrength ("Area Variation Strength", Range(0, 1)) = 0.8
        _AreaScale ("Patch Size (patches per metre)", Range(0.1, 6)) = 0.9
        _AreaThreshold ("Patch Threshold (higher = more thin areas)", Range(0, 1)) = 0.45
        _AreaSoftness ("Patch Edge Softness", Range(0.01, 0.5)) = 0.2

        [Header(Surface)]
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3.0
        _FresnelBrightness ("Fresnel Brightness", Range(0, 2)) = 0.35
        _Smoothness ("Highlight Smoothness", Range(0, 1)) = 0.6
        _SpecIntensity ("Highlight Intensity", Range(0, 2)) = 0.4
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
            Name "FrostedGlass"
            Tags { "LightMode" = "UniversalForward" }

            // The pass writes the already-blurred backdrop itself, so it replaces what is behind
            // it rather than blending. Alpha only exists to let the milky layer sit on top.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _TintColor;
                float _TintStrength;
                float _Opacity;
                float _Coverage;
                float _BlurRadius;
                float _FrostNoiseScale;
                float _FrostDistortion;
                float4 _FogColor;
                float _Condensation;
                float _StreakDensity;
                float _StreakLength;
                float _StreakClarity;
                float _DropScaleSmall;
                float _DropScaleLarge;
                float _DropRefraction;
                float _Sparkle;
                float _DoodleAmount;
                float _DoodleSize;
                float _DoodleWidth;
                float _DoodleClarity;
                float _AreaStrength;
                float _AreaScale;
                float _AreaThreshold;
                float _AreaSoftness;
                float _FresnelPower;
                float _FresnelBrightness;
                float _Smoothness;
                float _SpecIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float  fogCoord    : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);

                OUT.positionHCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = nrm.normalWS;
                OUT.fogCoord = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float VNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x),
                            lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y);
            }

            // One layer of round droplets. mask = lens coverage, lensOffset = refraction direction,
            // spark = tiny bright glint on each drop.
            void Droplets(float2 p, float scale, float coverage, out float mask, out float2 lensOffset, out float spark)
            {
                float2 g = p * scale;
                float2 id = floor(g);
                float2 f = frac(g) - 0.5;
                float2 c = (float2(Hash21(id + 1.7), Hash21(id + 9.2)) - 0.5) * 0.45;
                float present = step(1.0 - coverage, Hash21(id + 5.1));
                float h = Hash21(id);
                float r = lerp(0.10, 0.30, h * h);
                float2 d = f - c;
                // slightly stretched downward, like a drop that has started to slide
                d.y *= 0.8;
                mask = smoothstep(r, r * 0.65, length(d)) * present;
                lensOffset = -d / r * mask;
                spark = smoothstep(r * 0.35, 0.0, length(d - float2(-0.25, 0.3) * r)) * mask;
            }

            float Dot2(float2 v) { return dot(v, v); }

            // Heart signed distance (negative inside), tip at the origin, about 1.2 tall.
            float SdHeartRaw(float2 p)
            {
                p.x = abs(p.x);
                if (p.y + p.x > 1.0)
                    return sqrt(Dot2(p - float2(0.25, 0.75))) - 0.35355339;
                return sqrt(min(Dot2(p - float2(0.0, 1.0)), Dot2(p - 0.5 * max(p.x + p.y, 0.0))))
                       * sign(p.x - p.y);
            }

            // Heart in doodle units (roughly -1..1 wide), centred at c, scaled by s. Returns distance in doodle units.
            float SdHeart(float2 u, float2 c, float s)
            {
                float2 h = (u - c) / s * 0.6 + float2(0.0, 0.5);
                return SdHeartRaw(h) / 0.6 * s;
            }

            float SdSeg(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h);
            }

            // Distance (doodle units, radius ~1) to the strokes of doodle number 'type'.
            float DoodleDist(float2 u, int type)
            {
                float d;
                if (type == 0)
                {
                    // plain heart outline
                    d = abs(SdHeart(u, float2(0, 0), 1.0));
                }
                else if (type == 1)
                {
                    // heart pierced by an arrow
                    float2 a = float2(-1.15, -0.6), b = float2(1.15, 0.7);
                    float2 dir = normalize(b - a);
                    float2 nrm = float2(-dir.y, dir.x);
                    d = abs(SdHeart(u, float2(0, 0.0), 0.85));
                    d = min(d, SdSeg(u, a, b));
                    d = min(d, SdSeg(u, b, b - dir * 0.3 + nrm * 0.2));
                    d = min(d, SdSeg(u, b, b - dir * 0.3 - nrm * 0.2));
                    d = min(d, SdSeg(u, a + dir * 0.05, a + dir * 0.3 + nrm * 0.2));
                    d = min(d, SdSeg(u, a + dir * 0.05, a + dir * 0.3 - nrm * 0.2));
                    d = min(d, SdSeg(u, a + dir * 0.25, a + dir * 0.5 + nrm * 0.2));
                    d = min(d, SdSeg(u, a + dir * 0.25, a + dir * 0.5 - nrm * 0.2));
                }
                else if (type == 2)
                {
                    // big heart with a small one floating off its shoulder
                    d = abs(SdHeart(u, float2(-0.15, -0.1), 0.8));
                    d = min(d, abs(SdHeart(u, float2(0.7, 0.65), 0.3)));
                }
                else if (type == 5)
                {
                    // smiley
                    d = abs(length(u) - 0.9);
                    d = min(d, SdSeg(u, float2(-0.3, 0.2), float2(-0.3, 0.32)));
                    d = min(d, SdSeg(u, float2(0.3, 0.2), float2(0.3, 0.32)));
                    float2 c = float2(0.0, 0.1);
                    d = min(d, max(abs(length(u - c) - 0.55), u.y - (c.y - 0.12)));
                }
                else if (type == 6)
                {
                    // five-point star, drawn as a pentagram in one go
                    d = 10.0;
                    [unroll]
                    for (int k = 0; k < 5; k++)
                    {
                        float a0 = 1.5707963 + k * 1.2566371;
                        float a1 = 1.5707963 + (k + 2) * 1.2566371;
                        d = min(d, SdSeg(u, 0.95 * float2(cos(a0), sin(a0)), 0.95 * float2(cos(a1), sin(a1))));
                    }
                }
                else if (type == 7)
                {
                    // "<3"
                    d = SdSeg(u, float2(-0.3, 0.5), float2(-0.95, 0.0));
                    d = min(d, SdSeg(u, float2(-0.95, 0.0), float2(-0.3, -0.5)));
                    float2 ct = float2(0.45, 0.27), cb = float2(0.45, -0.27);
                    d = min(d, max(abs(length(u - ct) - 0.27), ct.x - 0.08 - u.x));
                    d = min(d, max(abs(length(u - cb) - 0.27), cb.x - 0.08 - u.x));
                    d = min(d, SdSeg(u, ct + float2(0.0, 0.27), ct + float2(-0.3, 0.27)));
                    d = min(d, SdSeg(u, cb + float2(0.0, -0.27), cb + float2(-0.3, -0.27)));
                }
                else if (type == 8)
                {
                    // infinity: love forever
                    d = min(abs(length(u - float2(-0.5, 0.0)) - 0.5),
                            abs(length(u - float2(0.5, 0.0)) - 0.5));
                }
                else if (type == 9)
                {
                    // heart with little rays of love
                    d = abs(SdHeart(u, float2(0.0, -0.3), 0.7));
                    d = min(d, SdSeg(u, float2(0.0, 0.6), float2(0.0, 0.95)));
                    d = min(d, SdSeg(u, float2(-0.5, 0.5), float2(-0.75, 0.8)));
                    d = min(d, SdSeg(u, float2(0.5, 0.5), float2(0.75, 0.8)));
                }
                else
                {
                    // concentric heart with a little sparkle plus
                    d = abs(SdHeart(u, float2(-0.1, -0.1), 0.85));
                    d = min(d, abs(SdHeart(u, float2(-0.1, -0.05), 0.4)));
                    float2 s = float2(0.8, 0.8);
                    d = min(d, SdSeg(u, s + float2(-0.2, 0), s + float2(0.2, 0)));
                    d = min(d, SdSeg(u, s + float2(0, -0.2), s + float2(0, 0.2)));
                }
                return d;
            }

            // Fingertip graffiti: one jittered, rotated, hand-wobbled doodle per grid cell.
            // Returns stroke coverage (1 = fully wiped) and a soft rim for the water ridge.
            void Doodles(float2 p, out float cover, out float rim)
            {
                float2 g = p / _DoodleSize;
                float2 id0 = floor(g);
                float2 wob = (float2(VNoise(p * 38.0), VNoise(p * 38.0 + 11.0)) - 0.5) * 0.09;
                float w = _DoodleWidth * 0.5;

                cover = 0;
                rim = 0;
                // Each cell may hold one doodle placed anywhere inside it (and spilling over its edge),
                // with its own size, tilt and mirroring, so the grid never reads. Check the 3x3 neighbourhood.
                [loop]
                for (int j = -1; j <= 1; j++)
                {
                    [loop]
                    for (int i = -1; i <= 1; i++)
                    {
                        float2 id = id0 + float2(i, j);
                        if (Hash21(id + 31.7) < 1.0 - _DoodleAmount) continue;

                        float2 centre = id + 0.5 + (float2(Hash21(id + 3.3), Hash21(id + 8.8)) - 0.5) * 0.8;
                        float R = lerp(0.18, 0.4, Hash21(id + 5.5));
                        float ang = (Hash21(id + 12.9) - 0.5) * 1.6;
                        int type = (int)floor(Hash21(id + 21.1) * 9.0);
                        type += (type >= 3) ? 1 : 0; // number 3 is retired
                        float flip = Hash21(id + 17.4) < 0.5 ? -1.0 : 1.0;

                        float2 q = (g - centre) / R;
                        if (dot(q, q) > 2.0) continue;
                        float sn, cs;
                        sincos(ang, sn, cs);
                        q = float2(cs * q.x + sn * q.y, -sn * q.x + cs * q.y);
                        q.x *= flip;
                        q += wob;

                        float d = DoodleDist(q, type) * R * _DoodleSize;
                        float c = smoothstep(w, w * 0.55, d);
                        cover = max(cover, c);
                        rim = max(rim, smoothstep(w * 2.2, w, d) * (1.0 - c));
                    }
                }
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float2 uv = GetNormalizedScreenSpaceUV(IN.positionHCS);

                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // Pane-local coordinates in metres: horizontal along the glass, vertical = world Y.
                // The mesh UVs are unusable, so the pattern is projected from world space.
                float3 T = normalize(float3(-N.z, 0, N.x));
                float2 p = float2(dot(IN.positionWS, T), IN.positionWS.y);

                // Per-pixel grain, in world space so it stays stuck to the glass instead of
                // swimming with the camera -- in VR the swim would read immediately.
                float2 grainCoord = IN.positionWS.xy + IN.positionWS.zz * 0.7;
                float2 grain = float2(Hash21(grainCoord * _FrostNoiseScale),
                                      Hash21(grainCoord * _FrostNoiseScale + 17.3)) * 2.0 - 1.0;

                // --- condensation mask: 1 = fogged, 0 = clear ---
                // Drip streaks: vertical columns, each with its own random run of clear glass.
                // Each column holds separate drips: a fat bead at the bottom and a thin, wobbly,
                // tapering trail above it where the bead wiped the fog away. Starts, lengths and
                // sizes are random per drip, and the trail wavers sideways like a real run.
                float colX = p.x * _StreakDensity;
                float cid = floor(colX);
                float yc = p.y * _StreakLength + Hash21(float2(cid, 4.4)) * 17.0;
                float seg = floor(yc);
                float t = frac(yc);
                float2 dk = float2(cid, seg);
                float exists = step(0.25, Hash21(dk + 2.3));
                float tHead = lerp(0.08, 0.35, Hash21(dk + 6.1));
                float tEnd = min(0.98, tHead + lerp(0.25, 0.85, Hash21(dk + 7.7)));

                float rh = lerp(0.2, 0.34, Hash21(dk + 1.1)) / _StreakDensity;     // bead radius, metres
                float ym = (t - tHead) / _StreakLength;
                // Meander measured from the bead so the bead always sits at the end of its own trail:
                // a slow sway plus a faster jitter, growing the further the trail runs back up.
                float seed = Hash21(dk + 9.9) * 50.0;
                float sway = (VNoise(float2(ym * 3.5 + seed, 1.7)) - VNoise(float2(seed, 1.7))) * 2.4
                           + (VNoise(float2(ym * 11.0 + seed, 8.3)) - VNoise(float2(seed, 8.3))) * 0.7;
                float wander = sway * 1.3 * rh * saturate(ym * _StreakLength * 4.0);
                float xm = (frac(colX) - 0.5) / _StreakDensity
                         + (Hash21(float2(cid, 3.1)) - 0.5) * 0.15 / _StreakDensity - wander;

                // Bead: a drop slightly longer than wide, with a hint of a tail.
                float2 hd = float2(xm, ym * 0.75) / rh;
                float headMask = smoothstep(1.0, 0.7, length(hd)) * exists;
                // Trail: narrows and wanders towards the top, fades out at its end.
                float k = saturate((t - tHead) / max(tEnd - tHead, 0.001));
                float trailW = rh * lerp(0.9, 0.3, k) * (1.0 + 0.25 * sin(p.y * 90.0 + cid));
                float trail = smoothstep(trailW, trailW * 0.45, abs(xm))
                            * step(0.0, ym) * smoothstep(1.0, 0.75, k) * exists;
                float streak = trail;

                float mL, mS, spL, spS;
                float2 offL, offS;
                Droplets(p, _DropScaleLarge, 0.45, mL, offL, spL);
                Droplets(p + 3.3, _DropScaleSmall, 0.6, mS, offS, spS);
                float dropMask = saturate(max(mL + mS, headMask));
                float2 headLens = -hd * headMask;
                float2 lens = offL * mL + offS * mS * (1.0 - mL) + headLens * (1.0 - mL);
                float headSpark = smoothstep(0.45, 0.0, length(hd - float2(-0.3, 0.45))) * headMask;
                float spark = saturate(spL + spS + headSpark);

                float doodle, doodleRim;
                Doodles(p, doodle, doodleRim);
                // water pushed aside by the finger piles up along the stroke
                spark = saturate(spark + doodleRim * 0.6);

                float clearAmt = saturate(max(max(streak * _StreakClarity, dropMask), doodle * _DoodleClarity));
                // uneven fog so it is never a flat veil
                float patch = lerp(0.8, 1.0, VNoise(p * 5.0));
                float fog = lerp(1.0, (1.0 - clearAmt) * patch, _Condensation);

                // Large-scale noise that thins the frost out in soft patches (0 = clear, 1 = full effect).
                float2 aq = p * _AreaScale;
                float areaN = VNoise(aq) * 0.6 + VNoise(aq * 2.3 + 7.0) * 0.3 + VNoise(aq * 5.1 + 3.0) * 0.1;
                float area = smoothstep(_AreaThreshold - _AreaSoftness, _AreaThreshold + _AreaSoftness, areaN);
                fog *= lerp(1.0, area, _AreaStrength);

                // Golden-angle spiral blur, rotated per pixel so 16 taps don't band.
                const int TAPS = 16;
                const float GOLDEN = 2.39996323;
                float rot = Hash21(grainCoord * _FrostNoiseScale + 3.7) * 6.2831853;
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float blurR = _BlurRadius * fog;
                float2 refractOff = lens * _DropRefraction * (1.0 - fog);

                half3 acc = 0;
                [unroll]
                for (int i = 0; i < TAPS; i++)
                {
                    float r = sqrt((i + 0.5) / TAPS) * blurR;
                    float a = i * GOLDEN + rot;
                    float2 off = float2(cos(a), sin(a)) * r;
                    off.x /= aspect;
                    acc += SampleSceneColor(uv + off + grain * _FrostDistortion * fog + refractOff);
                }
                half3 backdrop = acc / TAPS;

                half3 tint = lerp(backdrop, backdrop * _TintColor.rgb, _TintStrength);

                float fres = pow(1.0 - saturate(dot(N, V)), _FresnelPower);

                Light mainLight = GetMainLight();
                float3 H = normalize(mainLight.direction + V);
                float specPower = exp2(10.0 * _Smoothness + 1.0);
                half3 spec = pow(saturate(dot(N, H)), specPower) * mainLight.color * _SpecIntensity;

                // Fogged areas pick up the warm glow of whatever light is behind them.
                half lum = dot(backdrop, half3(0.299, 0.587, 0.114));
                half3 milk = _FogColor.rgb * (0.5 + 0.5 * mainLight.color) * (0.25 + 2.0 * lum);
                half3 col = tint + fres * _FresnelBrightness * _FogColor.rgb + spec;
                col += spark * _Sparkle * (backdrop + 0.15) * _FogColor.rgb;

                // Alpha decides how much of the *sharp* scene is replaced by the blurred backdrop.
                // Drops replace it (so the refraction shows), clear streaks leave the real scene.
                half fogAlpha = saturate(_Coverage + _Opacity * 0.5);
                half alpha = lerp(dropMask, fogAlpha, fog);
                col = lerp(col, milk, _Opacity * 0.5 * fog);

                col = MixFog(col, IN.fogCoord);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
