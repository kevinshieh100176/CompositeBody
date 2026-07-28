#if UNITY_RENDER_PIPELINES_UNIVERSAL
using System;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_RENDER_PIPELINES_UNIVERSAL_RENDERGRAPH
using System.Collections.Generic;
using UnityEngine.Rendering.RenderGraphModule;
#endif
using UnityEngine.Rendering.Universal;

namespace Unity.XR.CompositionLayers.Emulation
{
    public class EmulationLayerUniversalScriptableRendererPass : ScriptableRenderPass
    {
        const RenderPassEvent UnderlayRenderPassEvent = RenderPassEvent.BeforeRenderingGbuffer;
        const RenderPassEvent OverlayRenderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

#if UNITY_RENDER_PIPELINES_UNIVERSAL_RENDERGRAPH
        ProfilingSampler m_ProfilingSampler;
        readonly Dictionary<RenderPassEvent, string> RenderPassNames = new Dictionary<RenderPassEvent, string>()
        {
            { UnderlayRenderPassEvent, "XR Composition Layer (Underlay)" },
            { OverlayRenderPassEvent,  "XR Composition Layer (Overlay)" }
        };
#endif
        class PassData
        {
            public RenderPassEvent renderPassEvent;
            public Camera camera;
        }

        public EmulationLayerUniversalScriptableRendererPass(RenderPassEvent renderPassEvent)
        {
            this.renderPassEvent = renderPassEvent;
#if UNITY_RENDER_PIPELINES_UNIVERSAL_RENDERGRAPH
            m_ProfilingSampler = new ProfilingSampler(RenderPassNames[renderPassEvent]);
#endif
        }

#if !UNITY_6000_4_OR_NEWER
        [Obsolete("Execute is deprecated as of XR Composition Layers 2.2, and will be removed in Unity 6.4. At your own risk, you can set URP_COMPATIBILITY_MODE in your project's scripting defines if you require this API.")]
#pragma warning disable CS0809
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
#pragma warning restore CS0809
        {
            var camera = renderingData.cameraData.camera;
            var commandArgs = new EmulatedLayerData.CommandArgs(camera);
            var layerDataList = EmulationLayerScriptableRendererManager.GetEmulatedLayerDataList(IsOverlay(renderPassEvent));
            foreach (var layerData in layerDataList)
            {
                if (IsSupported(layerData, camera))
                {
                    var commandBuffer = layerData.UpdateCommandBuffer(commandArgs);
                    context.ExecuteCommandBuffer(commandBuffer);
                }
            }
        }
#else
        [Obsolete("URP Compatibility Mode is removed in Unity 6.4. You must upgrade your project to Render Graph.", true)]
#pragma warning disable CS0114
        public void Execute(ScriptableRenderContext context, ref RenderingData renderingData) { }
#pragma warning restore CS0114
#endif

#if UNITY_RENDER_PIPELINES_UNIVERSAL_RENDERGRAPH
        // 17.0.0 or newer
        static void ExecutePass(PassData data, RasterGraphContext rgContext)
        {
            var camera = data.camera;
            var commandArgs = new EmulatedLayerData.CommandArgs(camera);
            var layerDataList = EmulationLayerScriptableRendererManager.GetEmulatedLayerDataList(IsOverlay(data.renderPassEvent));
            foreach (var layerData in layerDataList)
            {
                if (IsSupported(layerData, camera))
                    layerData.AddToCommandBuffer(rgContext.cmd, commandArgs);
            }
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(RenderPassNames[renderPassEvent], out var passData, m_ProfilingSampler))
            {
                passData.renderPassEvent = renderPassEvent;
                passData.camera = cameraData.camera;
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
                builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) => ExecutePass(data, rgContext));
            }
        }
#endif

        static EmulationLayerUniversalScriptableRendererPass s_UnderlayPass;
        static EmulationLayerUniversalScriptableRendererPass s_OverlayPass;

        internal static bool s_InjectPassRegistered;

        static bool IsOverlay(RenderPassEvent renderPassEvent) => renderPassEvent == OverlayRenderPassEvent;

        static bool IsSupported(EmulatedLayerData layerData, Camera camera) => EmulatedLayerProvider.IsSupported(camera) && layerData.IsSupported(camera);


        internal static void RegisterScriptableRendererPass()
        {
            if (!s_InjectPassRegistered)
            {
                s_InjectPassRegistered = true;
                RenderPipelineManager.beginCameraRendering += InjectPass;
            }
        }

        internal static void UnregisterScriptableRendererPass()
        {
            if (s_InjectPassRegistered)
            {
                s_InjectPassRegistered = false;
                RenderPipelineManager.beginCameraRendering -= InjectPass;
            }
        }

        static void InjectPass(ScriptableRenderContext renderContext, Camera currCamera)
        {
            var data = currCamera.GetUniversalAdditionalCameraData();
            if (data != null && data.scriptableRenderer != null)
            {
#pragma warning disable CS0618
                data.scriptableRenderer.EnqueuePass(UnderlayPass);
                data.scriptableRenderer.EnqueuePass(OverlayPass);
#pragma warning restore CS0618
            }
        }

        static EmulationLayerUniversalScriptableRendererPass UnderlayPass
        {
            get
            {
                if(s_UnderlayPass == null)
                    s_UnderlayPass = new EmulationLayerUniversalScriptableRendererPass(UnderlayRenderPassEvent);

                return s_UnderlayPass;
            }
        }

        static EmulationLayerUniversalScriptableRendererPass OverlayPass
        {
            get
            {
                if (s_OverlayPass == null)
                    s_OverlayPass = new EmulationLayerUniversalScriptableRendererPass(OverlayRenderPassEvent);

                return s_OverlayPass;
            }
        }
    }
}
#endif
