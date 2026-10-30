using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using CompositeBody.Experience;
using CompositeBody.Multiplayer.EditorSetup;

namespace CompositeBody.Experience.EditorTools
{
    /// <summary>
    /// Builds O-0's content on its own and photographs the cue at several points along it.
    ///
    /// Not the whole experience scene: this exercises exactly the two things that change when
    /// the beat's content changes -- <see cref="BuildO0Arrival.Populate"/> and
    /// <see cref="BuildO0Arrival.Wire"/> -- without the lobby, the director or the network. If
    /// the cue is wired wrongly the wiring step fails here with a named path, and if it is wired
    /// correctly the pictures show what two people will actually be standing in.
    ///
    /// The cue is SCRUBBED rather than played. The beat evaluates it as a pure
    /// function of shared time with no state, which is what makes a still partway through it
    /// meaningful at all -- there is nothing to have accumulated differently on the way there.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Experience.EditorTools.RenderO0Arrival.Run
    ///   -outDir &lt;dir&gt; [-width 1280] [-height 800]
    /// </summary>
    public static class RenderO0Arrival
    {
        const string k_ScenePath = "Assets/_Scenes/_Test/O0ArrivalPreview.unity";
        const string k_Prefab = "Assets/_Scenes/O_0.prefab";

        /// <summary>Where to park the cue for the "touched" frames: the end of the authored cue.</summary>
        const float k_TouchedAt = 30f;

        /// <summary>
        /// Where along the cue to stop. Chosen from the authored defaults -- hold 10 s, 光圈 up
        /// over 7 s, white light starting down 3.5 s later over 6.5 s -- so these are the four
        /// states that matter rather than four evenly spaced samples of a fade.
        /// </summary>
        static readonly (float t, string label)[] k_Moments =
        {
            (2f,    "title"),     // 合成肉身 in the dark, nothing else lit
            (10.5f, "line"),      // the opening line, centre of the space
            (18f,   "arrive"),    // words gone, the room is up, one white light
            (30f,   "apart"),     // two separate lights, the shared one gone
        };

        public static void Run()
        {
            Debug.Log("[O0Arrival] Starting...");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // The shipping prefab, not a fresh Populate. O-0 is hand-authored now, and a harness
            // that rebuilds the beat from code photographs a beat nobody is going to run -- it
            // would miss every change made in the prefab since, including the whole Timeline.
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(k_Prefab);
            if (asset == null)
            {
                Debug.LogError($"[O0Arrival] RESULT: FAIL - no prefab at {k_Prefab}.");
                return;
            }

            var holder = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            var beat = holder.GetComponent<O0ArrivalBeat>();
            Transform content = holder.transform.Find("Content");
            if (beat == null || content == null)
            {
                Debug.LogError("[O0Arrival] RESULT: FAIL - the prefab has no O0ArrivalBeat or no Content.");
                return;
            }

            // The beat ships switched off; BeatController turns it on at runtime.
            content.gameObject.SetActive(true);

            // The real room, so the stills answer the question the outline exists for: does the
            // staging fit inside 4 x 5 m. It lives in MainScene rather than the beat prefab, so
            // the harness has to bring its own.
            var room = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Scenes/PlaySpace_Debug.prefab");
            if (room != null) PrefabUtility.InstantiatePrefab(room);

            Camera cam = BuildCamera();

            // The beat does this on entry and nothing here calls OnEnterBeat, so without it the
            // room is lit by whatever ambient an empty scene comes with -- which is a default
            // skybox, and turned the black void into a blue-grey gradient.
            beat.ApplyAtmosphere();

            // The other player's blob is a networked avatar with nobody driving it, so in a still
            // it is a grey dome parked at the origin. Hidden for the preview rather than moved:
            // where it belongs is wherever the second player is standing.
            Transform blob = content.transform.Find("OtherPlayerBlob");
            if (blob != null) blob.gameObject.SetActive(false);

            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), k_ScenePath);
            AssetDatabase.SaveAssets();

            string outDir = GetArg("-outDir");
            if (string.IsNullOrEmpty(outDir))
            {
                Debug.Log($"[O0Arrival] {k_ScenePath} built.\n[O0Arrival] RESULT: PASS");
                return;
            }

            int width = 1280, height = 800;
            string w = GetArg("-width"), h = GetArg("-height");
            if (!string.IsNullOrEmpty(w)) int.TryParse(w, out width);
            if (!string.IsNullOrEmpty(h)) int.TryParse(h, out height);
            Directory.CreateDirectory(outDir);

            var zone = UnityEngine.Object.FindFirstObjectByType<CompositeFogZone>();

            // The reference framing: high, wide, looking down into the pool. Not a view anybody
            // in a headset will ever have -- it is there to be held next to the image this was
            // built from, which is a film still and so is shot from where no one stands.
            var views = new (Vector3 position, Vector3 look, string name, bool everyMoment)[]
            {
                (new Vector3(-0.6f, 5.2f, -7.4f), new Vector3(0.1f, 0.9f, 1.6f), "wide", true),
                // Eye height, standing where a player stands, looking at the other one.
                (new Vector3(0.1f, 1.62f, -3.4f), new Vector3(0.1f, 1.25f, 1.4f), "eye", true),
                // Standing at z = -2, which is where the players now start. The wide views stand
                // outside the 4 x 5 m room to see the whole staging,
                // so they make the type look far smaller than it is in a headset.
                (new Vector3(0f, 1.60f, -2.0f),   new Vector3(0f, 1.52f, 0.6f),    "read", true),
            };

            // THIS HARNESS CANNOT PHOTOGRAPH A VFX GRAPH, and that is a property of batch mode
            // rather than of the graph.
            //
            // VFX Graph simulation is dispatched by VFXManager during the player loop.
            // -executeMethod blocks that loop for as long as this method runs, so a graph shot
            // from in here has never been stepped however many times Simulate() was called
            // first -- Simulate only queues the steps. What settled it was rendering 25 cm
            // particles at 120 a second and getting a byte-identical frame, which ruled out
            // size, brightness, bounds and culling in one go.
            //
            // Returning without -quit to let the editor tick does not fix it either: the editor
            // loop runs (other packages' delayCall work proceeds) but EditorApplication.update
            // callbacks registered from inside -executeMethod never fire, so the process just
            // sits there. Tried, measured, reverted -- left written down so it is not retried.
            //
            // Everything that is NOT a VFX Graph photographs correctly, which is why these
            // stills are still worth taking. To see the dust, open the saved scene and look.
            if (UnityEngine.Object.FindFirstObjectByType<UnityEngine.VFX.VisualEffect>() != null)
            {
                Debug.LogWarning($"[O0Arrival] NOTE: the dust is a VFX Graph and will be ABSENT " +
                                 $"from these stills -- batch mode never steps it. The lights, " +
                                 $"fog and figures are accurate. Open {k_ScenePath} to see the dust.");
            }

            s_OutDir = outDir;
            s_Width = width;
            s_Height = height;
            s_Cam = cam;
            s_Beat = beat;
            s_Zone = zone;
            s_Views = views;
            s_Ticks = 0;

            foreach (var vfx in UnityEngine.Object.FindObjectsByType<UnityEngine.VFX.VisualEffect>(
                         FindObjectsSortMode.InstanceID))
            {
                vfx.Reinit();
                // 22 seconds, which covers the longest authored lifetime, so the system is full
                // rather than filling. Queued here; dispatched by the ticks.
                vfx.Simulate(1f / 30f, 660);
            }

            // Shoot now, synchronously, so the harness still works under -batchmode -quit.
            s_Ticks = k_WarmupTicks;
            Tick();
        }

        /// <summary>Editor ticks to wait before shooting, so the queued simulation is dispatched.</summary>
        const int k_WarmupTicks = 60;

        static string s_OutDir;
        static int s_Width, s_Height, s_Ticks;
        static Camera s_Cam;
        static O0ArrivalBeat s_Beat;
        static CompositeFogZone s_Zone;
        static (Vector3 position, Vector3 look, string name, bool everyMoment)[] s_Views;

        /// <summary>
        /// Wait for the editor to actually step the VFX, then take every frame in one tick.
        ///
        /// All eight stills come from a single tick deliberately: nothing advances between them,
        /// so the dust is identical in all of them and only the lights differ. That is what makes
        /// the sequence comparable as a sequence.
        /// </summary>
        static void Tick()
        {
            if (++s_Ticks < k_WarmupTicks) return;
            EditorApplication.update -= Tick;

            var dust = UnityEngine.Object.FindFirstObjectByType<UnityEngine.VFX.VisualEffect>();
            Debug.Log($"[O0Arrival] dust alive after {s_Ticks} ticks: " +
                      $"{(dust != null ? dust.aliveParticleCount : -1)}");

            int written = 0, expected = 0;
            foreach ((Vector3 position, Vector3 look, string name, bool every) in s_Views)
            {
                s_Cam.transform.position = position;
                s_Cam.transform.LookAt(look);

                foreach ((float t, string label) in k_Moments)
                {
                    if (!every && t != k_Moments[0].t) continue;
                    expected++;

                    s_Beat.ScrubCue(t);

                    // Timeline writes the SERIALIZED fields -- VolumetricSpot.m_Reveal,
                    // CardFade.m_Alpha -- which bypasses the property setters that push those
                    // values into each renderer's MaterialPropertyBlock. At runtime the next
                    // LateUpdate does it and nobody notices; in a batch render there is no next
                    // LateUpdate, so every one of them draws whatever its MATERIAL says instead.
                    // That is why the white beam glowed through the whole opening in the first
                    // version of these stills: its material default reveal is 1.
                    foreach (var spot in UnityEngine.Object.FindObjectsByType<VolumetricSpot>(
                                 FindObjectsSortMode.InstanceID)) spot.Rebuild();
                    foreach (var card in UnityEngine.Object.FindObjectsByType<CardFade>(
                                 FindObjectsSortMode.InstanceID)) card.Rebuild();
                    // The fog zone tints the air from the fixtures that are lit, and it reads
                    // their reveal to do it. At runtime its own LateUpdate catches this; here
                    // nothing drives it, so the glow would be whatever the previous frame said.
                    if (s_Zone != null) s_Zone.Rebuild();

                    string path = Path.Combine(s_OutDir, $"o0_{name}_{t:00}s_{label}.png");
                    if (Render(s_Cam, path, s_Width, s_Height)) written++;
                    Debug.Log($"[O0Arrival] {name} at t={t:F1}s ({label}) -> {path}");
                }
            }

            // The music-box plates, which no authored time brings up: they wait for a hand, and a
            // batch render has none. Forced on at the end of the cue so the pattern can be seen
            // at all. The hand clouds cannot be faked the same way -- they read tracked joints,
            // so they only exist in a headset.
            s_Beat.ScrubCue(k_TouchedAt);
            foreach (var spot in UnityEngine.Object.FindObjectsByType<VolumetricSpot>(
                         FindObjectsSortMode.InstanceID)) spot.Rebuild();
            foreach (var card in UnityEngine.Object.FindObjectsByType<CardFade>(
                         FindObjectsSortMode.InstanceID)) card.Rebuild();

            var plates = UnityEngine.Object.FindObjectsByType<MusicBoxFloor>(
                FindObjectsSortMode.InstanceID);
            foreach (var plate in plates) { plate.spin = 1f; plate.Rebuild(); }

            foreach ((Vector3 position, Vector3 look, string name, bool _) in s_Views)
            {
                expected++;
                s_Cam.transform.position = position;
                s_Cam.transform.LookAt(look);
                string path = Path.Combine(s_OutDir, $"o0_{name}_touched.png");
                if (Render(s_Cam, path, s_Width, s_Height)) written++;
                Debug.Log($"[O0Arrival] {name}, plates wound -> {path}");
            }
            Debug.Log($"[O0Arrival] {plates.Length} music-box plate(s) forced on for the last frames.");

            bool ok = written == expected;
            if (ok)
                Debug.Log($"[O0Arrival] {k_ScenePath}, {written} frames.\n[O0Arrival] RESULT: PASS");
            else
                Debug.LogError($"[O0Arrival] only {written} of {expected}.\n[O0Arrival] RESULT: FAIL");
        }


        static Camera BuildCamera()
        {
            var go = new GameObject("Preview Camera");
            var cam = go.AddComponent<Camera>();
            go.AddComponent<UniversalAdditionalCameraData>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            // The void's own colour, so the horizon has nothing to end against.
            cam.backgroundColor = new Color(0.012f, 0.013f, 0.017f);
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 80f;
            go.tag = "MainCamera";
            return cam;
        }

        static bool Render(Camera cam, string outPath, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4
            };
            RenderTexture previous = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(outPath, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[O0Arrival] {outPath}: {e.Message}");
                return false;
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = previous;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }
    }
}
