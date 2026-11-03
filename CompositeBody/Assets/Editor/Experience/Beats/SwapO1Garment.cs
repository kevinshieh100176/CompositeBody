using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using CompositeBody.Experience;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Puts O-1's second object -- 鬼的抓取2, 「搶衣服」 -- into the prefab the show actually runs,
    /// as the T-shirt the running order asked for rather than the jacket that stood in for it.
    ///
    /// WHY THIS IS NOT JUST A RE-RUN OF <see cref="BuildO1Grab"/>. That builder writes to
    /// Assets/_Scenes/O_1.prefab, and there is no such file any more -- the beats were gathered
    /// into Onboarding.prefab and it was left behind. Its table of props is still read by
    /// <see cref="BuildO1Box"/> and <see cref="BuildO1Timeline"/>, which do work on the live
    /// prefab, so the names in it still matter; what it can no longer do is build anything. So
    /// this changes the object INSIDE the station that exists, and leaves the station itself
    /// alone.
    ///
    /// WHAT IT DELIBERATELY DOES NOT TOUCH, because every one of them is wired by hand or by
    /// another builder and would be lost by rebuilding the station:
    /// the O1GrabStation and whether it carries a grab cue, the RoleVisibility that hides the
    /// other player's copy, the PropPresence the dissolve is driven through, the Rigidbody and
    /// XRGrabInteractable settings, the box watcher that points at this object, and the wave in
    /// O1GrabWaves that brings it in. The object keeps its identity -- same GameObject, same
    /// components -- and only its mesh, its collider, its material, its size and its name
    /// change.
    ///
    /// THE MESH IS LOD00 AND THE LODGroup IS LEFT BEHIND. SmallProps carries this one as a
    /// prefab with two LODs; a prop held at arm's length does not need the second, and an
    /// LODGroup inside a grabbable is one more thing that can switch a renderer off while
    /// somebody is holding it.
    ///
    /// Idempotent: it finds the station under either name, so running it twice is a no-op.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.SwapO1Garment.Run
    /// </summary>
    public static class SwapO1Garment
    {
        const string k_Prefab = "Assets/_Scenes/Onboarding.prefab";
        const string k_Group = "O_1 Grab";

        /// <summary>The prop being put in, read from the table both live builders share.</summary>
        static BuildO1Grab.Prop Garment => BuildO1Grab.Props[1];

        /// <summary>What the station was called before, so this can find it once and then again.</summary>
        static readonly (string before, string after)[] k_Stations =
        {
            ("Jacket (Player A)", "T-shirt (Player A)"),
            ("Jacket (Player B)", "T-shirt (Player B)"),
        };

        [MenuItem("Tools/O-1/Swap the garment for the T-shirt")]
        public static void Run()
        {
            Debug.Log("[O1Garment] Swapping O-1's second object...");

            Mesh mesh = LoadMesh(Garment.model);
            if (mesh == null) return;

            // The tinted, clipped pair, built the same way BuildO1Grab and BuildO1Timeline build
            // every other prop's: the owner's 代表色 on the base colour, and the dissolve noise
            // in the base map's alpha for PropPresence to clip against.
            var materials = new Material[2];
            for (int i = 0; i < 2; i++)
            {
                PlayerRole role = i == 0 ? PlayerRole.Player1 : PlayerRole.Player2;
                materials[i] = BuildMaterial(role);
                if (materials[i] == null) return;

                if (!BuildO1Timeline.MakePropClippable(Garment.Material(role))) return;
            }

            AssetDatabase.SaveAssets();

            GameObject root = PrefabUtility.LoadPrefabContents(k_Prefab);
            if (root == null)
            {
                Debug.LogError($"[O1Garment] RESULT: FAIL - could not open {k_Prefab}.");
                return;
            }

            try
            {
                Transform group = FindDeep(root.transform, k_Group);
                if (group == null)
                {
                    Debug.LogError($"[O1Garment] RESULT: FAIL - no '{k_Group}' in the prefab.");
                    return;
                }

                for (int i = 0; i < k_Stations.Length; i++)
                {
                    (string before, string after) = k_Stations[i];

                    Transform station = FindDeep(group, after) ?? FindDeep(group, before);
                    if (station == null)
                    {
                        Debug.LogError($"[O1Garment] RESULT: FAIL - no station '{before}' or " +
                                       $"'{after}' under {k_Group}.");
                        return;
                    }

                    if (!Swap(station, mesh, materials[i])) return;
                    station.name = after;
                }

                RenameWave(group);

                PrefabUtility.SaveAsPrefabAsset(root, k_Prefab);
                Debug.Log("[O1Garment] RESULT: PASS");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// The object inside one station: its mesh, its collider, its material, its size and
        /// where it floats.
        ///
        /// WHERE IT FLOATS IS KEPT BY ITS BOTTOM EDGE, not by its pivot and not by its centre.
        /// 「物件出現 浮在箱子正上方」: the jacket was a flat thing lying in the air just above the
        /// carton's mouth, and a shirt on a hanger is four times as tall -- centring the new
        /// bounds where the old ones were would push half of it down through the lid. So the x
        /// and z centres are kept and the new object stands on the height the old one's
        /// underside was at, which leaves the carton clear and the thing still in reach.
        /// </summary>
        static bool Swap(Transform station, Mesh mesh, Material material)
        {
            var grab = station.GetComponentInChildren<XRGrabInteractable>(true);
            if (grab == null)
            {
                Debug.LogError($"[O1Garment] RESULT: FAIL - nothing grabbable under {station.name}.");
                return false;
            }

            Transform prop = grab.transform;

            var filter = prop.GetComponent<MeshFilter>();
            var renderer = prop.GetComponent<MeshRenderer>();
            if (filter == null || renderer == null)
            {
                Debug.LogError($"[O1Garment] RESULT: FAIL - '{prop.name}' is not a single mesh " +
                               "object; this tool swaps the mesh in place and cannot rebuild a " +
                               "hierarchy.");
                return false;
            }

            Bounds was = renderer.bounds;

            filter.sharedMesh = mesh;
            renderer.sharedMaterial = material;

            // Real metres along the longest axis, measured at scale 1 rather than taken from the
            // model: it does not matter what units it was exported in.
            prop.localScale = Vector3.one;
            Bounds unit = renderer.bounds;
            float longest = Mathf.Max(unit.size.x, Mathf.Max(unit.size.y, unit.size.z));
            float scale = longest > 1e-5f ? Garment.size / longest : 1f;
            prop.localScale = Vector3.one * scale;

            // The pivot of these models is nowhere near the mesh, so the object is moved by the
            // difference between where its bounds ended up and where they are asked to be.
            Bounds now = renderer.bounds;
            prop.position += new Vector3(was.center.x - now.center.x,
                                         was.min.y - now.min.y,
                                         was.center.z - now.center.z);

            var collider = prop.GetComponent<MeshCollider>();
            if (collider == null) collider = prop.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = true;

            // Named after the object, because the debug panel's rows and ids are: the id for a
            // station is o1.grab.<role>.<the grabbable's name>, so a player hearing "Grab the
            // Jacket" while holding a T-shirt is a row nobody can act on.
            prop.name = mesh.name.Replace("_LOD00", "");

            RenameWatcher(station, grab);

            Bounds after = renderer.bounds;
            Debug.Log($"[O1Garment] {station.name}: {mesh.name} at scale {scale:F4} for " +
                      $"{Garment.size:F2} m, {material.name}; bounds {after.size.ToString("F3")} " +
                      $"centred {after.center.ToString("F3")} (was {was.size.ToString("F3")} at " +
                      $"{was.center.ToString("F3")}).");
            return true;
        }

        /// <summary>
        /// The box watcher waiting for this object, which is named after it -- "Drop (Jacket)".
        ///
        /// IT HAS TO BE RENAMED OR THE NEXT BOX BUILD DELETES IT. <see cref="BuildO1Box"/> makes
        /// one watcher per prop called "Drop ({key})" and removes any other it finds, on the
        /// sound argument that a watcher for an object the beat no longer has would hold the
        /// beat open. Left as "Drop (Jacket)" this one is exactly that to it -- so a re-run
        /// would destroy the object O1DropSequence holds in its list of boxes and leave a hole
        /// in it. Renaming costs a line and the watcher keeps its place, its wiring and its
        /// references.
        ///
        /// Found by what it watches, not by its name: this must work on the run after the
        /// rename as well as the one before it.
        /// </summary>
        static void RenameWatcher(Transform station, XRGrabInteractable grab)
        {
            // SEARCHED FROM THE GROUP, NOT FROM THE STATION. There is one carton per player and
            // it hangs off that player's FIRST station -- the lamp's -- with a watcher under it
            // for every object they have to put away. So the T-shirt's watcher is not under the
            // T-shirt's station, and looking there finds nothing and silently renames nothing.
            Transform group = station.parent != null ? station.parent : station;

            foreach (O1DropBox watcher in group.GetComponentsInChildren<O1DropBox>(true))
            {
                if (watcher.target != grab) continue;

                string name = $"Drop ({Garment.key})";
                if (watcher.name == name) return;

                Debug.Log($"[O1Garment] '{watcher.name}' is '{name}' now, so the next box build " +
                          "keeps it rather than taking it for a watcher of something gone.");
                watcher.name = name;
                return;
            }
        }

        /// <summary>
        /// The wave's own label, which is what the log and the debug panel call this object
        /// while the beat is running. Left reading "Jacket" it would name the thing in the
        /// player's hands wrongly in the one place staff look when it is not in their hands.
        /// </summary>
        static void RenameWave(Transform group)
        {
            var waves = group.GetComponentInChildren<O1GrabWaves>(true);
            if (waves == null) return;

            var so = new SerializedObject(waves);
            SerializedProperty list = so.FindProperty("m_Waves");

            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty wave = list.GetArrayElementAtIndex(i);
                SerializedProperty name = wave.FindPropertyRelative("name");
                if (name.stringValue != "Jacket") continue;

                name.stringValue = Garment.key;
                Debug.Log($"[O1Garment] wave {i + 1} is called '{Garment.key}' now.");
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The mesh to put in: the highest-detail LOD of the model, or its only mesh when it has
        /// no LODs. Taken off the asset rather than instantiated, because the station already
        /// has the object that carries it.
        /// </summary>
        static Mesh LoadMesh(string modelPath)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogError($"[O1Garment] RESULT: FAIL - no model at {modelPath}.");
                return null;
            }

            Mesh best = null;
            int most = -1;

            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;

                // LOD00 by name where the model says so, and the densest mesh otherwise -- which
                // is the same thing, and survives a model whose LODs are named differently.
                int score = filter.sharedMesh.vertexCount;
                if (filter.name.EndsWith("_LOD00")) score += 1000000;
                if (score <= most) continue;

                most = score;
                best = filter.sharedMesh;
            }

            if (best == null)
                Debug.LogError($"[O1Garment] RESULT: FAIL - no mesh under {modelPath}.");

            return best;
        }

        /// <summary>
        /// One role's copy of the model's material, tinted with that player's 代表色. The same
        /// shape as BuildO1Grab's: copied rather than written property by property, so whatever
        /// the model came textured with is what the room shows.
        /// </summary>
        static Material BuildMaterial(PlayerRole role)
        {
            string path = Garment.Material(role);
            var source = AssetDatabase.LoadAssetAtPath<Material>(Garment.source);

            if (source == null)
            {
                Debug.LogError($"[O1Garment] RESULT: FAIL - no source material at {Garment.source}.");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source.shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = source.shader;
            material.CopyPropertiesFromMaterial(source);
            material.shaderKeywords = source.shaderKeywords;

            Color tint = PlayerRoleColors.For(role);
            tint.a = 1f;
            material.SetColor("_BaseColor", tint);

            EditorUtility.SetDirty(material);
            return material;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;

            foreach (Transform child in root)
            {
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
