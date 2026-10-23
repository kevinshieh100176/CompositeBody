Shader "CompositeBody/AvatarBody"
{
    Properties
    {
        [MainColor] _BaseColor("Base Colour", Color) = (0.085, 0.082, 0.088, 1)
        [MainTexture] _BaseTexture("Base Texture", 2D) = "white" {}
        _Metallic("Metallic", Range(0, 1)) = 0
        _Smoothness("Smoothness", Range(0, 1)) = 0

        [Header(Dither by distance)]
        _MinDistance("Min Distance", Float) = 0.2
        _MaxDistance("Max Distance", Float) = 0.8

        [Header(Legs)]
        _LegFadeStart("Fade start height (m)", Float) = 0.5
        _LegFadeSoftness("Fade softness (m)", Range(0.001, 1)) = 0.15
    }

    // Ch36's body, dithered away where it should not be seen.
    //
    // REPLACES SimpleLitDither for the body only, and keeps that graph's behaviour exactly,
    // because something already depends on it: AvatarFormation fades the body in at the touch by
    // driving _MaxDistance, and XRAvatarVisuals swaps these materials in so a player does not see
    // the inside of their own head. Both rest on the same rule -- alpha rises with distance from
    // the camera, between _MinDistance and _MaxDistance, and is then dithered and clipped -- so
    // pushing the far threshold out hides the body and bringing it back in fades it on. That
    // sentence is from AvatarFormation's own summary and is the specification this was written
    // against.
    //
    // WHY NOT EDIT THE GRAPH. It is a vendor asset of 70KB of JSON, and the thing wanted is one
    // more term on the alpha. Hand-editing a graph to add a multiply risks a beat that currently
    // works for a change that is three lines in text.
    //
    // THE LEGS ARE CUT IN OBJECT SPACE, by height up the body. A skinned mesh is one renderer and
    // cannot have part of it switched off, but its vertices arrive at the shader already skinned
    // and in the renderer's own space, where y is height above the avatar's origin -- which for
    // these Mixamo rigs is the floor between the feet. So the cut travels with the player, holds
    // while they walk, and does not need to know where the playspace has moved to.
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "Queue" = "Geometry"
        }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _BaseTexture_ST;
            float  _Metallic;
            float  _Smoothness;
            float  _MinDistance;
            float  _MaxDistance;
            float  _LegFadeStart;
            float  _LegFadeSoftness;
        CBUFFER_END

        /// How much of this pixel survives, before dithering.
        ///
        /// Two independent reasons to disappear, multiplied: being too close to the camera, which
        /// is the behaviour inherited from SimpleLitDither, and being below the knee.
        float BodyCoverage(float3 positionWS, float3 positionOS, float3 objectOriginWS)
        {
            float distanceToBody = distance(_WorldSpaceCameraPos, objectOriginWS);
            float span = max(_MaxDistance - _MinDistance, 1e-4);
            float byDistance = saturate((distanceToBody - _MinDistance) / span);

            // Softened over _LegFadeSoftness BELOW the start height, so the named height is where
            // the body is fully present rather than half of it.
            float byHeight = smoothstep(_LegFadeStart - _LegFadeSoftness, _LegFadeStart,
                                        positionOS.y);

            return byDistance * byHeight;
        }

        /// The 4x4 ordered threshold Shader Graph's Dither node uses, so the grain matches.
        float DitherThreshold(float4 positionCS)
        {
            const float thresholds[16] =
            {
                1.0 / 17.0,  9.0 / 17.0,  3.0 / 17.0, 11.0 / 17.0,
               13.0 / 17.0,  5.0 / 17.0, 15.0 / 17.0,  7.0 / 17.0,
                4.0 / 17.0, 12.0 / 17.0,  2.0 / 17.0, 10.0 / 17.0,
               16.0 / 17.0,  8.0 / 17.0, 14.0 / 17.0,  6.0 / 17.0
            };
            uint index = (uint(positionCS.x) % 4) * 4 + uint(positionCS.y) % 4;
            return thresholds[index];
        }

        void ClipBody(float3 positionWS, float3 positionOS, float3 objectOriginWS, float4 positionCS)
        {
            clip(BodyCoverage(positionWS, positionOS, objectOriginWS) - DitherThreshold(positionCS));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseTexture);
            SAMPLER(sampler_BaseTexture);

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
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float2 uv         : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
                float3 originWS   : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                // Carries which eye this vertex belongs to into the fragment stage. Both eyes are
                // drawn in one instanced pass, and without this the fragment stage shades
                // whichever eye happened to be set last.
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseTexture);
                OUT.fogFactor = ComputeFogFactor(p.positionCS.z);

                // The renderer's own origin, which is what the distance dither measures to -- the
                // whole body appears and disappears together rather than a sphere of it.
                OUT.originWS = TransformObjectToWorld(float3(0, 0, 0));
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                ClipBody(IN.positionWS, IN.positionOS, IN.originWS, IN.positionCS);

                half4 albedo = SAMPLE_TEXTURE2D(_BaseTexture, sampler_BaseTexture, IN.uv) * _BaseColor;

                InputData input = (InputData)0;
                input.positionWS = IN.positionWS;
                input.normalWS = normalize(IN.normalWS);
                input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                input.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                input.fogCoord = IN.fogFactor;
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                input.bakedGI = SampleSH(input.normalWS);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo.rgb;
                surface.metallic = _Metallic;
                surface.smoothness = _Smoothness;
                surface.occlusion = 1.0;
                surface.alpha = 1.0;

                half4 colour = UniversalFragmentPBR(input, surface);
                colour.rgb = MixFog(colour.rgb, IN.fogFactor);
                return colour;
            }
            ENDHLSL
        }

        // The silhouette has to match in every pass, or the body casts the shadow of legs it is
        // not drawing and fills depth where it is not there.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float3 originWS   : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 toLight = normalize(_LightPosition - positionWS);
            #else
                float3 toLight = _LightDirection;
            #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, toLight));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif

                OUT.positionCS = positionCS;
                OUT.positionWS = positionWS;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.originWS = TransformObjectToWorld(float3(0, 0, 0));
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                ClipBody(IN.positionWS, IN.positionOS, IN.originWS, IN.positionCS);
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

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float3 originWS   : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.originWS = TransformObjectToWorld(float3(0, 0, 0));
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                ClipBody(IN.positionWS, IN.positionOS, IN.originWS, IN.positionCS);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
