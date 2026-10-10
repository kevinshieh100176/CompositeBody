using UnityEditor;
using UnityEngine;
using CompositeBody.Experience;
using CompositeBody.PointClouds;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Adds O-0's touch interaction into O_0.prefab: a music-box plate under each 真人 and under
    /// the player, two hand clouds, and the component that watches for a hand.
    ///
    /// Scaffolding, like the rest of the O-0 builders -- it writes the objects once with sane
    /// values and then the prefab is yours. Re-running it is safe and will not move anything that
    /// already exists.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildO0Touch.Run
    /// </summary>
    public static class BuildO0Touch
    {
        const string k_Prefab = "Assets/_Scenes/O_0.prefab";
        const string k_PlateMat = "Assets/Materials/O0MusicBox.mat";
        const string k_HandMat = "Assets/Materials/O0HandCloud.mat";

        const float k_PlateLift = 0.012f;      // clear of the floor, under the z-fight threshold
        const float k_FigurePlate = 1.7f;      // metres across
        const float k_PlayerPlate = 1.9f;

        [MenuItem("Tools/O-0/Build Touch Interaction")]
        static void RunFromMenu() => Run();

        public static void Run()
        {
            Debug.Log("[Touch] Starting...");

            var plateShader = Shader.Find("CompositeBody/MusicBoxFloor");
            if (!Check(plateShader, "CompositeBody/MusicBoxFloor")) return;
            var moteShader = Shader.Find("CompositeBody/TraceParticle");
            if (!Check(moteShader, "CompositeBody/TraceParticle")) return;

            Material plateMat = LoadOrCreate(k_PlateMat, plateShader, "O0MusicBox");
            plateMat.SetColor("_Color", new Color(0.55f, 0.60f, 0.78f, 1f));
            plateMat.SetFloat("_Intensity", 1.1f);
            plateMat.SetFloat("_Teeth", 18f);
            plateMat.SetFloat("_Rings", 11f);
            EditorUtility.SetDirty(plateMat);

            Material handMat = LoadOrCreate(k_HandMat, moteShader, "O0HandCloud");
            handMat.SetColor("_Color", new Color(0.80f, 0.83f, 0.95f, 1f));
            handMat.SetFloat("_Intensity", 0.9f);
            EditorUtility.SetDirty(handMat);

            GameObject root = PrefabUtility.LoadPrefabContents(k_Prefab);
            if (root == null)
            {
                Debug.LogError($"[Touch] RESULT: FAIL - could not open {k_Prefab}.");
                return;
            }

            try
            {
                Transform content = root.transform.Find("Content");
                if (content == null)
                {
                    Debug.LogError("[Touch] RESULT: FAIL - the prefab has no Content.");
                    return;
                }

                Transform interaction = Ensure(content, "Interaction");

                // A plate under each 真人, parented to them so it travels when they are moved.
                var stations = new System.Collections.Generic.List<(PointCloudFigure f, MusicBoxFloor p)>();
                foreach (string name in new[] { "Human A", "Human B" })
                {
                    Transform station = content.Find(name);
                    if (station == null)
                    {
                        Debug.LogError($"[Touch] RESULT: FAIL - no '{name}' under Content.");
                        return;
                    }

                    var figure = station.GetComponentInChildren<PointCloudFigure>(true);
                    if (figure == null)
                    {
                        Debug.LogError($"[Touch] RESULT: FAIL - '{name}' has no PointCloudFigure.");
                        return;
                    }

                    MusicBoxFloor plate = BuildPlate(station, "Plate", plateMat, k_FigurePlate);
                    stations.Add((figure, plate));
                }

                MusicBoxFloor playerPlate = BuildPlate(interaction, "Player Plate", plateMat, k_PlayerPlate);

                Transform hands = Ensure(interaction, "Hands");
                HandCloud left = BuildHand(hands, "Left", handMat, UnityEngine.XR.Hands.Handedness.Left);
                HandCloud right = BuildHand(hands, "Right", handMat, UnityEngine.XR.Hands.Handedness.Right);

                var touch = interaction.GetComponent<O0TouchAwakening>();
                if (touch == null) touch = interaction.gameObject.AddComponent<O0TouchAwakening>();

                var so = new SerializedObject(touch);
                SerializedProperty list = so.FindProperty("m_Stations");
                list.arraySize = stations.Count;
                for (int i = 0; i < stations.Count; i++)
                {
                    SerializedProperty entry = list.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("figure").objectReferenceValue = stations[i].f;
                    entry.FindPropertyRelative("floor").objectReferenceValue = stations[i].p;
                }
                so.FindProperty("m_PlayerFloor").objectReferenceValue = playerPlate;
                SerializedProperty handList = so.FindProperty("m_Hands");
                handList.arraySize = 2;
                handList.GetArrayElementAtIndex(0).objectReferenceValue = left;
                handList.GetArrayElementAtIndex(1).objectReferenceValue = right;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, k_Prefab);
                Debug.Log($"[Touch] {stations.Count} station plate(s), a player plate, 2 hand " +
                          $"clouds, and the watcher.\n[Touch] RESULT: PASS");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// A flat quad with the music-box shader. A Quad is the XY plane with its face along -Z,
        /// so +90 degrees about X lays it down facing up -- and the shader reads the plate in its
        /// own xy, which keeps the pattern independent of how it ends up rotated in the room.
        /// </summary>
        static MusicBoxFloor BuildPlate(Transform parent, string name, Material mat, float metres)
        {
            Transform existing = parent.Find(name);
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = name;
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.transform.SetParent(parent, false);
                go.transform.localPosition = new Vector3(0f, k_PlateLift, 0f);
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                go.transform.localScale = Vector3.one * metres;
            }

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var plate = go.GetComponent<MusicBoxFloor>();
            if (plate == null) plate = go.AddComponent<MusicBoxFloor>();
            return plate;
        }

        static HandCloud BuildHand(Transform parent, string name, Material mat,
                                   UnityEngine.XR.Hands.Handedness handedness)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null ? existing.gameObject : new GameObject(name);
            if (existing == null) go.transform.SetParent(parent, false);

            var system = go.GetComponent<ParticleSystem>();
            if (system == null) system = go.AddComponent<ParticleSystem>();

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;
            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 32 * 24;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.None;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var cloud = go.GetComponent<HandCloud>();
            if (cloud == null) cloud = go.AddComponent<HandCloud>();
            var so = new SerializedObject(cloud);
            so.FindProperty("m_Hand").enumValueIndex = (int)handedness;
            so.ApplyModifiedPropertiesWithoutUndo();
            return cloud;
        }

        static Transform Ensure(Transform parent, string name)
        {
            Transform t = parent.Find(name);
            if (t != null) return t;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static Material LoadOrCreate(string path, Shader shader, string name)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                if (existing.shader != shader) existing.shader = shader;
                return existing;
            }
            var mat = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static bool Check(Shader shader, string name)
        {
            if (shader == null)
            {
                Debug.LogError($"[Touch] RESULT: FAIL - shader '{name}' not found.");
                return false;
            }
            if (ShaderUtil.ShaderHasError(shader))
            {
                foreach (var m in ShaderUtil.GetShaderMessages(shader))
                    Debug.LogError($"[Touch] {name} {m.severity} line {m.line}: {m.message}");
                Debug.LogError($"[Touch] RESULT: FAIL - '{name}' has compile errors.");
                return false;
            }
            return true;
        }
    }
}
