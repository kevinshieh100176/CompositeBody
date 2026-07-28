using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Unity.XR.CompositionLayers.UIInteraction
{
    /// <summary>
    /// Helper class for InteractableUIMirror that helps with creating Render Textures of a world space canvas
    /// </summary>
    /// <seealso cref="InteractableUIMirror"/>
    internal class CameraTargetTextureFactory
    {
        const int MINIMUM_RENDER_TEXTURE_SIZE = 100;
        // Desired size of render texture
        Vector2 m_RenderTextureSize = new Vector2(500, 500);

        static readonly Dictionary<RenderTexture, Camera> s_CamerasByRenderTexture = new();

        /// <summary>
        /// Releases the Camera's Render Texture, then recreates with new Rect size
        /// </summary>
        /// <param name="camera">Camera to assign texutre to</param>
        /// <param name="rect">Rect to render</param>
        public void ReplaceTargetTexture(Camera camera, Rect rect)
        {
            ReleaseTargetTexture(camera);
            CreateTargetTexture(camera, rect);
        }

        /// <summary>
        /// Creates a new render texture to fit the supplied Rect, then assigns it to the Camera
        /// </summary>
        /// <param name="camera">Camera to assign texture to</param>
        /// <param name="rect">Rect to render</param>
        /// <returns></returns>
        public RenderTexture CreateTargetTexture(Camera camera, Rect rect)
        {
            ReleaseTargetTexture(camera);
            var largerDimension = Mathf.Max(rect.width, rect.height);
            var scale = Mathf.Max(1.0f, MINIMUM_RENDER_TEXTURE_SIZE / largerDimension);
            m_RenderTextureSize = new Vector2(rect.width * scale, rect.height * scale);
            var rt = new RenderTexture((int)m_RenderTextureSize.x, (int)m_RenderTextureSize.y, 24, RenderTextureFormat.ARGB32);
            rt.hideFlags |= HideFlags.DontSaveInEditor;
            camera.targetTexture = rt;
            s_CamerasByRenderTexture.Add(rt, camera);
            return rt;
        }

        /// <summary>
        /// Removes the current render texture assigned to the Camera
        /// </summary>
        /// <param name="camera">The camera to remove the texture from</param>
        /// <returns>Whether or not the texture was released</returns>
        public bool ReleaseTargetTexture(Camera camera)
        {
            if (camera == null || camera.targetTexture == null)
                return false;

            // If the texture already has a known camera that isn't this one, don't dispose of it.
            // (This can happen when a UI comp layer is duplicated.)
            if (s_CamerasByRenderTexture.ContainsKey(camera.targetTexture) && s_CamerasByRenderTexture[camera.targetTexture] != camera)
                return false;

            s_CamerasByRenderTexture.Remove(camera.targetTexture);

            var renderTexture = camera.targetTexture;
            camera.targetTexture = null;
            renderTexture.Release();
            if (Application.isPlaying)
                Object.Destroy(renderTexture);
            else
                Object.DestroyImmediate(renderTexture);

            CleanCameraRenderTexturesDictionary();

            return true;
        }

        void CleanCameraRenderTexturesDictionary()
        {
            foreach (var entry in s_CamerasByRenderTexture.Keys.ToList())
            {
                if (entry == null || s_CamerasByRenderTexture[entry] == null)
                    s_CamerasByRenderTexture.Remove(entry);
            }
        }
    }
}
