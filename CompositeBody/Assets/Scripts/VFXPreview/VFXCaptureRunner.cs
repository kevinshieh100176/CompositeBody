using System.Collections;
using System.IO;
using UnityEngine;

namespace VFXPreview
{
    /// <summary>
    /// Runs in play mode, lets a VFX warm up for a fixed time, then writes the
    /// camera's render target to a PNG and exits. Driving the capture from play
    /// mode means the effect is ticked and rendered by URP's normal loop, which
    /// batch-mode editor rendering does not do (effects stay culled there).
    /// </summary>
    public class VFXCaptureRunner : MonoBehaviour
    {
        public Camera targetCamera;
        public string outPath = "vfx_preview.png";
        public float warmupSeconds = 4.3f;
        public int size = 1024;
        public bool quitWhenDone = true;

        IEnumerator Start()
        {
            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1
            };
            rt.Create();
            // Let URP render this camera into the RT as part of its own loop.
            targetCamera.targetTexture = rt;

            var t = 0f;
            while (t < warmupSeconds)
            {
                t += Time.deltaTime;
                yield return null;
            }
            yield return new WaitForEndOfFrame();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            var full = Path.IsPathRooted(outPath)
                ? outPath
                : Path.Combine(Directory.GetCurrentDirectory(), outPath);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(full, tex.EncodeToPNG());

            var px = tex.GetPixels();
            double sum = 0; int lit = 0;
            foreach (var p in px)
            {
                double l = p.r * 0.299 + p.g * 0.587 + p.b * 0.114;
                sum += l;
                if (l > 0.02) lit++;
            }
            Debug.Log($"[VFXCapture] wrote {full} meanLuma={sum / px.Length:F5} " +
                      $"litPixels={lit} ({100.0 * lit / px.Length:F2}%)");

            targetCamera.targetTexture = null;
            rt.Release();

            if (quitWhenDone)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.Exit(0);
#else
                Application.Quit();
#endif
            }
        }
    }
}
