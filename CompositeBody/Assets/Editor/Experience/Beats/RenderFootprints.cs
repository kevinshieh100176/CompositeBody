using System.IO;
using UnityEditor;
using UnityEngine;
using CompositeBody.Experience;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Renders the footprint rings to PNGs, so what they look like can be judged without putting
    /// a headset on: the layout from above, and the view a player gets while the prints are still
    /// arriving.
    ///
    /// DRIVES THE REAL COMPONENT through <see cref="FootprintRing.reveal"/> -- the same entry
    /// point the clock uses at runtime -- rather than colouring quads here. A harness that drew
    /// its own version would agree with the show right up until it mattered.
    ///
    /// No play mode and no networking: the ring's reveal applies immediately, which is the payoff
    /// for having put the sweep behind one value.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.RenderFootprints.Run
    /// </summary>
    public static class RenderFootprints
    {
        const string k_PrefabPath = "Assets/_Scenes/Onboarding.prefab";
        const string k_OutDir = "Assets/_Renders/Footprints";
        const int k_Width = 900, k_Height = 700;

        [MenuItem("Tools/O-1/Render Footprints")]
        public static void Run()
        {
            Debug.Log("[FootRender] Starting...");
            Directory.CreateDirectory(k_OutDir);

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(k_PrefabPath);
            if (asset == null)
            {
                Debug.LogError($"[FootRender] RESULT: FAIL - no prefab at {k_PrefabPath}.");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            int written = 0;

            try
            {
                Transform footprints = instance.transform.Find("Content/Footprints");
                if (footprints == null)
                {
                    Debug.LogError("[FootRender] RESULT: FAIL - no Content/Footprints in the prefab.");
                    return;
                }

                // The show hides these with an activation track; nothing is driving one here.
                footprints.gameObject.SetActive(true);

                // EVERYTHING ELSE IN THE BEAT GOES. The first version rendered the whole prefab
                // and produced a picture of the O-0 room: the centre light's glow washes the
                // floor pink and the prints are nowhere in it. What is being judged here is the
                // prints, so the room is taken away rather than fought with.
                Isolate(instance.transform, footprints);

                var rings = footprints.GetComponentsInChildren<FootprintRing>(true);

                // Which way a print actually faces, read off the mesh rather than assumed.
                var probe = footprints.GetComponentInChildren<MeshFilter>(true);
                if (probe != null && probe.sharedMesh != null && probe.sharedMesh.normals.Length > 0)
                {
                    Vector3 world = probe.transform.TransformDirection(probe.sharedMesh.normals[0]);
                    var renderer = probe.GetComponent<MeshRenderer>();
                    Debug.Log($"[FootRender] print 0: mesh normal {probe.sharedMesh.normals[0]}, " +
                              $"world {world}, renderer enabled={renderer.enabled}, " +
                              $"material={renderer.sharedMaterial?.name}");
                }
                if (rings.Length == 0)
                {
                    Debug.LogError("[FootRender] RESULT: FAIL - no FootprintRing under Footprints.");
                    return;
                }

                // A floor to catch them, because the prints are additive light on a surface and a
                // ring floating in a void reads as a ring floating in a void. Dark, like the room.
                GameObject floor = BuildFloor();
                GameObject key = BuildLight();

                Camera camera = BuildCamera();

                // FOUR PHASES OF ONE LAP, because the cue is a loop and one still of a loop says
                // nothing about it. Read in order these show the band travelling: a bright head,
                // which is the next print to stand on, and a tail behind it going out.
                foreach (float phase in new[] { 0f, 0.25f, 0.5f, 0.75f })
                {
                    foreach (FootprintRing ring in rings) ring.phase = phase;
                    written += Shoot(camera, LookDown(camera),
                                     $"footprints_band_{Mathf.RoundToInt(phase * 100f):D2}_top") ? 1 : 0;
                }

                // From where the players are, which is the view the cue is actually given in.
                foreach (FootprintRing ring in rings) ring.phase = 0.25f;
                written += Shoot(camera, LookFromCentre(camera), "footprints_band_centre") ? 1 : 0;
                written += Shoot(camera, LookFromPlayer(camera), "footprints_band_eye") ? 1 : 0;

                // One print, close, so the silhouette itself can be judged. The phase puts print
                // 0 at full rather than halfway through coming up.
                foreach (FootprintRing ring in rings) ring.phase = 0.05f;
                written += Shoot(camera, LookClose(camera, rings[0]), "footprints_close") ? 1 : 0;

                Object.DestroyImmediate(floor);
                Object.DestroyImmediate(key);
                Object.DestroyImmediate(camera.gameObject);

                Debug.Log($"[FootRender] wrote {written} image(s) to {k_OutDir}");
                Debug.Log("[FootRender] RESULT: " + (written == 7 ? "PASS" : "FAIL"));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Switch off everything that is not on the path from the root down to the prints, so
        /// the shot contains the prints and nothing else.
        /// </summary>
        static void Isolate(Transform root, Transform keep)
        {
            for (Transform at = keep; at != null && at != root; at = at.parent)
            {
                Transform parent = at.parent;
                if (parent == null) break;

                foreach (Transform sibling in parent)
                {
                    if (sibling != at) sibling.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// A dim key light, because the floor below is a lit surface and with the room switched
        /// off there is nothing to light it. Low on purpose: the real room is nearly black and a
        /// brightly lit floor would flatter these prints in a way the show never will.
        /// </summary>
        static GameObject BuildLight()
        {
            var go = new GameObject("~RenderLight");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.35f;
            light.color = new Color(0.72f, 0.78f, 0.95f);
            light.shadows = LightShadows.None;
            go.transform.rotation = Quaternion.Euler(55f, 35f, 0f);
            return go;
        }

        /// <summary>A dark floor plane, so additive prints have something to sit on.</summary>
        static GameObject BuildFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "~RenderFloor";
            floor.transform.localScale = Vector3.one * 1.2f;       // 12 m across
            floor.transform.position = new Vector3(0f, -0.002f, 0f);
            Object.DestroyImmediate(floor.GetComponent<Collider>());

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                var material = new Material(lit) { name = "~RenderFloor" };
                material.SetColor("_BaseColor", new Color(0.035f, 0.035f, 0.042f));
                material.SetFloat("_Smoothness", 0.12f);
                floor.GetComponent<MeshRenderer>().sharedMaterial = material;
            }
            return floor;
        }

        static Camera BuildCamera()
        {
            var go = new GameObject("~FootCamera");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.008f, 0.008f, 0.012f);
            cam.nearClipPlane = 0.03f;
            return cam;
        }

        static Camera LookDown(Camera cam)
        {
            cam.orthographic = true;
            cam.orthographicSize = 2.2f;
            cam.transform.position = new Vector3(0f, 6f, 0f);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            return cam;
        }

        /// <summary>From the edge of the room, with the whole ring in frame.</summary>
        static Camera LookFromPlayer(Camera cam)
        {
            cam.orthographic = false;
            cam.fieldOfView = 55f;
            cam.transform.position = new Vector3(0f, 1.62f, -3.1f);
            cam.transform.LookAt(new Vector3(0f, 0f, -0.1f));
            return cam;
        }

        /// <summary>
        /// Standing in the middle, where the players are when the prints arrive, looking out at
        /// them. The view the cue is actually given in -- and the one that says whether a ring on
        /// the floor reads as somewhere to walk.
        /// </summary>
        static Camera LookFromCentre(Camera cam)
        {
            cam.orthographic = false;
            cam.fieldOfView = 70f;
            cam.transform.position = new Vector3(0f, 1.62f, -0.15f);
            cam.transform.rotation = Quaternion.Euler(38f, 0f, 0f);
            return cam;
        }

        static Camera LookClose(Camera cam, FootprintRing ring)
        {
            Transform print = ring.transform.childCount > 0 ? ring.transform.GetChild(0) : ring.transform;

            cam.orthographic = false;
            cam.fieldOfView = 40f;
            cam.transform.position = print.position + new Vector3(0.05f, 0.75f, -0.55f);
            cam.transform.LookAt(print.position);
            return cam;
        }

        static bool Shoot(Camera camera, Camera _, string name)
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

            Debug.Log($"[FootRender] {path}");
            return File.Exists(path);
        }
    }
}
