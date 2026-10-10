Shader "CompositeBody/VacuumMembrane"
{
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

            // Premultiplied alpha, so the streak highlights survive on a sheet that is mostly
            // see-through -- with straight SrcAlpha the specular is scaled down by the very
            // transparency that makes it read as film.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            // Both sides: seeing the far wall of the wrap through the near one is most of what
            // makes this read as a sheet enclosing a body rather than a coat of paint on it.
            // Draw order within the mesh is not depth sorted, so overlapping folds can composite
            // in the wrong order; at these alphas the error is not visible, but it is the reason
            // this is a haze and not a hard surface.
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

            // Ridged and stretched along one axis. Plastic film creases in long lines because
            // the sheet buckles along its whole length, and it is those long ridges -- not the
            // blobby dents a plain noise gives -- that catch the light as the streak highlights
            // the reference photographs are full of.
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

            struct Attributes
            {
                float4 positionOS : POSITION;   // skinned film surface
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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);

                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.restOS = IN.restPos;
                OUT.restWS = TransformObjectToWorld(IN.restPos);
                OUT.gap = IN.filmData.x;
                OUT.fogCoord = ComputeFogFactor(posInputs.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float3 N = normalize(IN.normalWS) * IS_FRONT_VFACE(facing, 1.0, -1.0);
                float3 V = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                // ---- Crumple ---------------------------------------------------------------
                // Sampled from the film's bind pose so the creases stay in the sheet instead of
                // swimming through it every time the body moves. The gradient is taken in 3D and
                // projected onto the surface, which sidesteps the tangent frame entirely -- and
                // with it every UV seam on a character mesh.
                float3 cp = IN.restOS * _CreaseScale;
                float c0 = MembraneCreaseField(cp);
                float e = 0.06;
                float3 cg = float3(MembraneCreaseField(cp + float3(e, 0, 0)) - c0,
                                   MembraneCreaseField(cp + float3(0, e, 0)) - c0,
                                   MembraneCreaseField(cp + float3(0, 0, e)) - c0) / e;
                cg = cg - N * dot(cg, N);
                N = normalize(N - cg * _CreaseStrength * 0.12);

                // ---- Contact ---------------------------------------------------------------
                // The sheet is optically coupled to the skin wherever it is actually touching:
                // no air layer, so almost nothing scatters and you read the body straight
                // through it. Where it bridges a gap it is a lit sheet with darkness behind,
                // and its own haze takes over and goes milky. That split is the whole look.
                // Floor subtracted first: the builder never lets the sheet closer than its own
                // minimum offset, so without it even film lying flat on skin reports a gap and
                // never reads as fully in contact.
                float contact = 1.0 - saturate((IN.gap - _ContactFloor) / max(_ContactRange, 1e-4));
                contact = pow(contact, _ContactSharpness);

                // Film pulled taut thins and clears a little further, so a limb pressing out
                // into the sheet reads sharper than the slack around it. Area Jacobian of
                // (bind pose -> skinned pose), straight off the rasteriser.
                float3 dPx = ddx(IN.positionWS), dPy = ddy(IN.positionWS);
                float3 dRx = ddx(IN.restWS),     dRy = ddy(IN.restWS);
                float areaRest = length(cross(dRx, dRy));
                float areaRatio = (areaRest > 1e-12) ? length(cross(dPx, dPy)) / areaRest : 1.0;
                float taut = saturate((clamp(areaRatio, 0.25, 4.0) - 1.0) * 2.0);
                contact = saturate(contact + taut * _TautClarity);

                float frost = 1.0 - contact;

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
                // Edge-on, the view travels through far more film, so the rim of every fold
                // opaques up. This is what draws the bright outlines around the creases.
                alpha = saturate(alpha + fresnel * _EdgeOpacity);

                // A thin sheet scatters from both sides, so it is lit even facing away.
                float wrapped = saturate((NoL + 0.55) / 1.55);
                float3 color = film * wrapped * mainLight.color * atten * (0.35 + frost * 0.65);
                color += film * SampleSH(N) * _AmbientIntensity;

                // Light coming through the sheet from behind: the reason a lit film glows
                // rather than silhouetting.
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

                // Premultiply the body of the film, then add the highlights at full strength.
                float3 outRGB = color * alpha;
                outRGB += spec;
                outRGB += _SpecColor2.rgb * fresnel * _FresnelIntensity * alpha;

                outRGB = CompositeFogMix(outRGB, IN.positionWS, IN.fogCoord);
                return half4(outRGB, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
