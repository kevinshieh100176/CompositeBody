using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CompositeBody.Experience;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Renders the onboarding's two timed sequences to PNGs, so the look can be judged from
    /// stills instead of by putting a headset on and waiting forty seconds per attempt.
    ///
    /// It drives the REAL components through their own progress property -- the same entry point
    /// the clock uses at runtime -- rather than reproducing what they do. A harness that animated
    /// its own copy would agree with the show right up until the moment it mattered.
    ///
    /// No networking and no play mode. AvatarFormation and ScanHandover both expose a 0-to-1
    /// progress that applies immediately, which is exactly what makes them renderable this way;
    /// it is the payoff for having put the timing behind one value.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.RenderOnboardingStills.Run
    /// </summary>
    public static class RenderOnboardingStills
    {
        const string k_Scene = "Assets/_Scenes/Onboarding.unity";
        const string k_OutDir = "Assets/_Renders/Onboarding";
        const int k_Width = 900, k_Height = 700;

        static readonly float[] k_Steps = { 0f, 0.25f, 0.5f, 0.75f, 1f };

        [MenuItem("Tools/Onboarding/Render Stills")]
        public static void Run()
        {
            Debug.Log("[Stills] Starting...");
            Directory.CreateDirectory(k_OutDir);

            EditorSceneManager.OpenScene(k_Scene, OpenSceneMode.Single);

            var handover = Object.FindFirstObjectByType<ScanHandover>(FindObjectsInactive.Include);
            if (handover == null)
            {
                Debug.LogError("[Stills] RESULT: FAIL - no ScanHandover in the scene.");
                return;
            }

            Camera camera = BuildCamera();
            int written = 0;

            // ARRIVED FIRST, OR THERE IS NOTHING TO PHOTOGRAPH. The prefab is authored at an
            // empty room now -- the 真人 are brought in by a clip on O-0's timeline, which does
            // not run here -- so every frame of the dissolve below would have come out empty.
            handover.SetArrivalForEditor(1f);

            // The handover: the 真人 dissolving and the object arriving in its place.
            foreach (float t in k_Steps)
            {
                handover.SetProgressForEditor(t);
                written += Shoot(camera, $"handover_{Mathf.RoundToInt(t * 100):D3}") ? 1 : 0;
            }

            handover.SetProgressForEditor(0f);

            // The orbiting lights, at four points around their loops. LightOrbit is driven from
            // ExperienceClock, which does not advance outside play mode, so the movers are
            // placed by hand here -- the trail and sparks need motion history and simulation, so
            // these show the glow only, which is the part that was flat.
            var orbits = Object.FindObjectsByType<LightOrbit>(FindObjectsInactive.Include,
                                                              FindObjectsSortMode.None);
            foreach (float t in new[] { 0f, 0.3f, 0.6f })
            {
                foreach (LightOrbit orbit in orbits) orbit.PreviewAt(t);
                written += Shoot(camera, $"orbit_{Mathf.RoundToInt(t * 100):D3}") ? 1 : 0;
            }

            Debug.Log($"[Stills] wrote {written} image(s) to {k_OutDir}\n" +
                      "[Stills] RESULT: " + (written >= k_Steps.Length ? "PASS" : "FAIL"));
        }

        /// <summary>
        /// Looking down the room from where the players stand, which is the view the stills are
        /// meant to stand in for.
        /// </summary>
        static Camera BuildCamera()
        {
            var go = new GameObject("~StillCamera");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.01f, 0.01f, 0.015f);
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 60f;
            go.transform.position = new Vector3(0f, 1.55f, -2.6f);
            go.transform.LookAt(new Vector3(0f, 1.0f, 0.6f));
            return cam;
        }

        static bool Shoot(Camera camera, string name)
        {
            var rt = new RenderTexture(k_Width, k_Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4
            };

            camera.targetTexture = rt;
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var shot = new Texture2D(k_Width, k_Height, TextureFormat.RGBA32, false);
            shot.ReadPixels(new Rect(0, 0, k_Width, k_Height), 0, 0);
            shot.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;

            string path = Path.Combine(k_OutDir, name + ".png");
            File.WriteAllBytes(path, shot.EncodeToPNG());

            Object.DestroyImmediate(shot);
            rt.Release();
            Object.DestroyImmediate(rt);

            Debug.Log($"[Stills] {path}");
            return File.Exists(path);
        }
    }
}
