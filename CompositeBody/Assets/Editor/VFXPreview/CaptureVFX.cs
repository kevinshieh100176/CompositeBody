using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.VFX;

namespace VFXPreview
{
    /// <summary>
    /// Builds a throwaway preview scene for a VFX asset and enters play mode;
    /// <see cref="VFXCaptureRunner"/> then writes the PNG and exits the editor.
    ///
    /// Launch WITHOUT -batchmode: in batch mode the effect stays culled and never
    /// simulates, so the capture comes back empty.
    /// </summary>
    public static class CaptureVFX
    {
        const string DefaultAsset = "Assets/VFX/PowderBurst.vfx";

        public static void PlayAndCapture()
        {
            string asset = DefaultAsset, outPath = "vfx_preview.png";
            float warmup = 4.3f, dist = 9f;
            int size = 1024;
            bool probe = false;

            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                bool hasNext = i < args.Length - 1;
                switch (args[i])
                {
                    case "--vfx": if (hasNext) asset = args[i + 1]; break;
                    case "--out": if (hasNext) outPath = args[i + 1]; break;
                    case "--warmup": if (hasNext) float.TryParse(args[i + 1], out warmup); break;
                    case "--dist": if (hasNext) float.TryParse(args[i + 1], out dist); break;
                    case "--size": if (hasNext) int.TryParse(args[i + 1], out size); break;
                    case "--probe": probe = true; break;
                }
            }

            Debug.Log($"[VFXCapture] setup asset={asset} out={outPath} warmup={warmup} dist={dist} size={size}");

            var vfxAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(asset);
            if (vfxAsset == null)
            {
                Debug.LogError($"[VFXCapture] FAILED to load VisualEffectAsset at {asset}");
                EditorApplication.Exit(2);
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGO = new GameObject("PreviewCamera");
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 500f;
            cam.allowHDR = true;
            cam.transform.position = new Vector3(0f, 0f, -dist);
            cam.transform.rotation = Quaternion.identity;

            if (probe)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.position = new Vector3(-2.6f, -2.6f, 0f);
                var unlit = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlit != null)
                    cube.GetComponent<Renderer>().sharedMaterial =
                        new Material(unlit) { color = Color.white };
            }

            var vfxGO = new GameObject("PreviewVFX");
            vfxGO.transform.position = Vector3.zero;
            var ve = vfxGO.AddComponent<VisualEffect>();
            ve.visualEffectAsset = vfxAsset;
            ve.initialEventName = "OnPlay";

            var runnerGO = new GameObject("CaptureRunner");
            var runner = runnerGO.AddComponent<VFXCaptureRunner>();
            runner.targetCamera = cam;
            runner.outPath = outPath;
            runner.warmupSeconds = warmup;
            runner.size = size;

            EditorApplication.EnterPlaymode();
        }
    }
}
