#if UNITY_RENDER_PIPELINES_UNIVERSAL
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.CompositionLayers.Rendering.Internals;
using Unity.XR.CompositionLayers.Services;
using Unity.XR.CompositionLayers.Extensions;
#if UNITY_RENDER_PIPELINES_UNIVERSAL_RENDERGRAPH
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
#endif

namespace Unity.XR.CompositionLayers.Emulation
{
    /// <summary>
    /// The custom render pass that applies the Color Scale and Bias effect.
    /// </summary>
    internal class EmulationColorScaleBiasPass : ScriptableRenderPass
    {
        const string k_PassName = "EmulatedColorScaleBiasPass";
        const string k_ShaderName = "Hidden/XRCompositionLayers/ColorScaleBias";
        const RenderPassEvent k_ColorScaleBiasRenderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        static readonly int k_ScaleParameterName = Shader.PropertyToID("_ColorScale");
        static readonly int k_BiasParameterName = Shader.PropertyToID("_ColorBias");
        static bool s_InjectPassRegistered;
        static EmulationColorScaleBiasPass s_ColorScaleBiasPass;

        Material m_EmulationMaterial;

#if !UNITY_6000_4_OR_NEWER && !UNITY_RENDER_PIPELINES_UNIVERSAL_RENDERGRAPH
        ProfilingSampler m_ProfilingSampler = new ProfilingSampler(k_PassName);
        RTHandle m_CameraColorTargetHandle;
        RTHandle m_TempRTHandle;
#endif

        /// <summary>
        /// Constructor to initialize the render pass with the appropriate render event.
        /// </summary>
        internal EmulationColorScaleBiasPass(RenderPassEvent renderPassEvent)
        {
            this.renderPassEvent = renderPassEvent;
            CreateEmulationMaterial();
#if UNITY_RENDER_PIPELINES_UNIVERSAL_RENDERGRAPH
            requiresIntermediateTexture = true;
#endif
        }

        /// <summary>
        /// Creates the material used for emulating color scale and bias.
        /// </summary>
        void CreateEmulationMaterial()
        {
            Shader shader = Shader.Find(k_ShaderName);
            if (shader != null)
            {
                m_EmulationMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                m_EmulationMaterial.SetVector(k_ScaleParameterName, Vector4.one);
                m_EmulationMaterial.SetVector(k_BiasParameterName, Vector4.zero);
            }
        }

        /// <summary>
        /// Checks if the ColorScaleBias extension exists in the current scene.
        /// </summary>
        /// <returns> Returns ture if compoenet is attached to Default Layer else false.</returns>
        bool IsExtensionInScene()
        {
            if (m_EmulationMaterial == null)
            {
                return false;
            }

            CompositionLayerManager compositionLayerManager = CompositionLayerManager.Instance;
            if (compositionLayerManager == null)
            {
                return false;
            }

            CompositionLayer defaultLayer = compositionLayerManager.DefaultSceneCompositionLayer;
            if (defaultLayer == null)
            {
                return false;
            }
            // If an XR device is connected and in use, do not apply the effect
            if(XRHelper.GetDeviceConnected())
            {
                return false;
            }

            ColorScaleBiasExtension colorScaleBiasExtension = defaultLayer.GetComponent<ColorScaleBiasExtension>();
            if (colorScaleBiasExtension == null || !colorScaleBiasExtension.isActiveAndEnabled)
            {
                return false;
            }

            // Apply the scale and bias values from the extension to the material
            m_EmulationMaterial.SetVector(k_ScaleParameterName, colorScaleBiasExtension.Scale);
            m_EmulationMaterial.SetVector(k_BiasParameterName, colorScaleBiasExtension.Bias);

            return true;
        }

#if UNITY_RENDER_PIPELINES_UNIVERSAL_RENDERGRAPH
        /// <summary>
        /// Handles the execution of the render pass in RenderGraph mode (Unity 6.0+).
        /// </summary>
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            if (resourceData.isActiveTargetBackBuffer || !IsSupported(cameraData.camera) || !IsExtensionInScene())
                return;

            TextureHandle source = resourceData.activeColorTexture;
            TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = k_PassName;
            destinationDesc.clearBuffer = true;
            destinationDesc.clearColor = Color.clear;

            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);
            RenderGraphUtils.BlitMaterialParameters para = new(source, destination, m_EmulationMaterial, 0);
            renderGraph.AddBlitPass(para, k_PassName);

            resourceData.cameraColor = destination;

        }
#else
        /// <summary>
        /// Configures the render pass and allocates a temporary render target.
        /// </summary>
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            RenderTextureDescriptor desc = cameraTextureDescriptor;
            desc.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref m_TempRTHandle, desc, FilterMode.Point, TextureWrapMode.Clamp,
                false, 1, 0, "_TempRT");
            ConfigureTarget(m_TempRTHandle);
            ConfigureClear(ClearFlag.Color, Color.clear);

        }
#endif

        /// <summary>
        /// Executes the render pass, applying the color scale and bias for Unity 2022.3 and earlier.
        /// </summary>
#if !UNITY_6000_4_OR_NEWER && !UNITY_RENDER_PIPELINES_UNIVERSAL_RENDERGRAPH
#pragma warning disable 0809
        [Obsolete("Execute is deprecated as of XR Composition Layers 2.2, and will be removed in Unity 6.4. At your own risk, you can set URP_COMPATIBILITY_MODE in your project's scripting defines if you require this API.")]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
#pragma warning restore 0809
        {
            if (m_EmulationMaterial == null || !IsSupported(renderingData.cameraData.camera) || !IsExtensionInScene())
                return;

            CommandBuffer cmd = CommandBufferPool.Get(k_PassName);

            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                RTHandle cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
                Blitter.BlitCameraTexture(cmd, cameraColorTarget, m_TempRTHandle, m_EmulationMaterial, 0);
                Blitter.BlitCameraTexture(cmd, m_TempRTHandle, cameraColorTarget);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
#else
#pragma warning disable CS0114
        [Obsolete("URP Compatibility Mode is removed in Unity 6.4. You must upgrade your project to Render Graph.", true)]
        public void Execute(ScriptableRenderContext context, ref RenderingData renderingData) { }
#pragma warning restore CS0114
#endif

        /// <summary>
        /// Determines whether the given camera supports the effect.
        /// </summary>
        static bool IsSupported(Camera camera) => EmulatedLayerProvider.IsSupported(camera);

        /// <summary>
        /// Registers the scriptable render pass to be injected into the render pipeline.
        /// </summary>
        internal static void RegisterScriptableRendererPass()
        {
            if (!s_InjectPassRegistered)
            {
                s_InjectPassRegistered = true;
                RenderPipelineManager.beginCameraRendering += InjectPass;
            }
        }

        /// <summary>
        /// Unregisters the scriptable render pass from the render pipeline.
        /// </summary>
        internal static void UnregisterScriptableRendererPass()
        {
            if (s_InjectPassRegistered)
            {
                s_InjectPassRegistered = false;
                RenderPipelineManager.beginCameraRendering -= InjectPass;
            }
        }

        /// <summary>
        /// Injects the render pass into the camera rendering process.
        /// </summary>
        static void InjectPass(ScriptableRenderContext renderContext, Camera currCamera)
        {
            var data = currCamera.GetUniversalAdditionalCameraData();
            if (data != null && data.scriptableRenderer != null)
            {
#pragma warning disable CS0618
                data.scriptableRenderer.EnqueuePass(ColorScaleBiasPass);
#pragma warning restore CS0618
            }
        }

        /// <summary>
        /// Retrieves or creates an instance of the color scale bias pass.
        /// </summary>
        static EmulationColorScaleBiasPass ColorScaleBiasPass
        {
            get
            {
                if (s_ColorScaleBiasPass == null)
                {
                    s_ColorScaleBiasPass = new EmulationColorScaleBiasPass(k_ColorScaleBiasRenderPassEvent);
                }

                return s_ColorScaleBiasPass;
            }
        }
    }
}
#endif
