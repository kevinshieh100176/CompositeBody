Shader "CompositeBody/LocalFog"
{
    // Ray-marched ground fog that lives inside a box. Put it on a Cube, size the cube to the area to fill.
    // The fog is densest at the bottom of the box and thins out towards the top, drifts slowly,
    // fades out at the box sides so there are no hard edges, and stops at the geometry it meets.
    Properties
    {
        [Header(Look)]
        _FogColor ("Fog Colour", Color) = (0.80, 0.76, 0.74, 1)
        _Density ("Density at the floor (per metre)", Range(0, 8)) = 2.2
        _HeightFalloff ("Height Falloff", Range(0.2, 6)) = 1.8
        _Glow ("Base Glow (shows up in dark rooms)", Range(0, 1)) = 0.12
        _LightInfluence ("Light Influence", Range(0, 2)) = 1
        _SceneGlow ("Scene Glow (fog picks up bright things behind it)", Range(0, 2)) = 0.6

        [Header(Billows)]
        _NoiseScale ("Billow Size (1/m)", Range(0.1, 4)) = 0.7
        _NoiseStrength ("Billow Strength", Range(0, 1)) = 0.75
        _WindSpeed ("Drift Speed", Range(0, 1)) = 0.06

        [Header(Box)]
        _EdgeFade ("Side Edge Fade", Range(0.01, 0.5)) = 0.18
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "LocalFog"
            Tags { "LightMode" = "UniversalForward" }

            // Premultiplied: the pass outputs the fog's own light already multiplied by its coverage.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            // Only the box's back faces are drawn so the fog still works with the camera inside the box.
            // Occlusion by the scene is done by hand against the depth texture.
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _FogColor;
                float _Density;
                float _HeightFalloff;
                float _Glow;
                float _LightInfluence;
                float _SceneGlow;
                float _NoiseScale;
                float _NoiseStrength;
                float _WindSpeed;
                float _EdgeFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                return OUT;
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float VNoise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash31(i);
                float n100 = Hash31(i + float3(1, 0, 0));
                float n010 = Hash31(i + float3(0, 1, 0));
                float n110 = Hash31(i + float3(1, 1, 0));
                float n001 = Hash31(i + float3(0, 0, 1));
                float n101 = Hash31(i + float3(1, 0, 1));
                float n011 = Hash31(i + float3(0, 1, 1));
                float n111 = Hash31(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                            lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            float InterleavedGradientNoise(float2 pixel)
            {
                return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float2 uv = GetNormalizedScreenSpaceUV(IN.positionHCS);

                float3 camWS = GetCameraPositionWS();
                float3 dirWS = IN.positionWS - camWS;
                dirWS = normalize(dirWS);

                // Ray in the box's own space, where the box is the unit cube [-0.5, 0.5]^3.
                // The parameter t stays in world metres because dirWS is a unit vector.
                float3 ro = mul(unity_WorldToObject, float4(camWS, 1.0)).xyz;
                float3 rd = mul((float3x3)unity_WorldToObject, dirWS);
                rd += step(abs(rd), 1e-6) * 1e-6;   // avoid dividing by zero
                float3 inv = 1.0 / rd;
                float3 t0 = (-0.5 - ro) * inv;
                float3 t1 = (0.5 - ro) * inv;
                float3 tMin = min(t0, t1);
                float3 tMax = max(t0, t1);
                float tNear = max(max(tMin.x, tMin.y), max(tMin.z, 0.0));
                float tFar = min(min(tMax.x, tMax.y), tMax.z);

                // Stop at whatever solid thing is in front of the back of the box.
                float rawDepth = SampleSceneDepth(uv);
                float3 sceneWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float tScene = dot(sceneWS - camWS, dirWS);
                tFar = min(tFar, tScene);

                if (tFar <= tNear)
                    return 0;

                const int STEPS = 16;
                float dt = (tFar - tNear) / STEPS;
                float jitter = InterleavedGradientNoise(IN.positionHCS.xy);
                float2 wind = float2(1.0, 0.7) * _Time.y * _WindSpeed;

                float opticalDepth = 0;
                [loop]
                for (int i = 0; i < STEPS; i++)
                {
                    float t = tNear + dt * (i + jitter);
                    float3 pOS = ro + rd * t;
                    float3 pWS = camWS + dirWS * t;

                    float h = saturate(pOS.y + 0.5);                       // 0 at the bottom of the box, 1 at the top
                    float heightD = pow(saturate(1.0 - h), _HeightFalloff);
                    float edge = saturate(min(0.5 - abs(pOS.x), 0.5 - abs(pOS.z)) / _EdgeFade);

                    float3 np = pWS * _NoiseScale + float3(wind.x, 0.0, wind.y);
                    float n = VNoise3(np) * 0.65 + VNoise3(np * 2.3 + 11.0) * 0.35;
                    float billow = lerp(1.0, saturate(n * 1.9 - 0.2), _NoiseStrength);

                    opticalDepth += _Density * heightD * edge * billow * dt;
                }

                float transmittance = exp(-opticalDepth);
                float coverage = 1.0 - transmittance;

                // Fog colour: lit by the sky, by the main light and by a little of whatever is behind it.
                Light mainLight = GetMainLight();
                half3 ambient = SampleSH(float3(0, 1, 0));
                half3 behind = 0;
                const float2 taps[4] = { float2(1, 0), float2(-1, 0), float2(0, 1), float2(0, -1) };
                [unroll]
                for (int k = 0; k < 4; k++)
                    behind += SampleSceneColor(uv + taps[k] * 0.02);
                behind *= 0.25;

                half3 lit = _FogColor.rgb * (ambient + mainLight.color * _LightInfluence * 0.5 + _Glow)
                          + behind * _SceneGlow * _FogColor.rgb;
                return half4(lit * coverage, coverage);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
