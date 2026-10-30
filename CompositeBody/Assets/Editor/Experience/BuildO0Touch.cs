using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CompositeBody.Experience;
using CompositeBody.PointClouds;
using CompositeBody.Avatar.Skin;

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

        const string k_AvatarPrefab = "Assets/VRMPAssets/Prefabs/PlayerPrefabs/XRI_Network_Player_Avatar.prefab";

        /// <summary>
        /// Put AvatarFormation on the player avatar, so the body can be withheld until the
        /// player has touched a 真人.
        ///
        /// On the avatar rather than in the beat: the avatar is spawned by netcode at runtime and
        /// does not exist to be wired from a scene. It registers itself as the local one when it
        /// spawns, and O0TouchAwakening asks for it by that.
        /// </summary>
        [MenuItem("Tools/O-0/Add Avatar Formation")]
        public static void AddAvatarFormation()
        {
            Debug.Log("[Formation] Starting...");

            GameObject root = PrefabUtility.LoadPrefabContents(k_AvatarPrefab);
            if (root == null)
            {
                Debug.LogError($"[Formation] RESULT: FAIL - could not open {k_AvatarPrefab}.");
                return;
            }

            try
            {
                var formation = root.GetComponent<AvatarFormation>();
                if (formation == null) formation = root.AddComponent<AvatarFormation>();

                // Every film gets its own MembraneReveal. The avatar ships with four films --
                // body, head and one per hand -- but only the body's had a reveal on it, so the
                // hands were already wrapped before anyone had touched a 真人. The hands are the
                // one part of themselves the player can see, so they are the part that had to
                // arrive on the touch rather than be there waiting.
                int added = 0, films = 0;
                foreach (var link in root.GetComponentsInChildren<MembraneFilmLink>(true))
                {
                    films++;
                    if (link.GetComponent<MembraneReveal>() != null) continue;
                    link.gameObject.AddComponent<MembraneReveal>();
                    added++;
                    Debug.Log($"[Formation] added MembraneReveal to '{link.name}'.");
                }

                // Re-found after the additions, so the component serialises with the full set
                // rather than relying on the runtime search to repeat the work every spawn.
                var reveals = root.GetComponentsInChildren<MembraneReveal>(true);
                var so = new SerializedObject(formation);
                SerializedProperty list = so.FindProperty("m_Membranes");
                list.arraySize = reveals.Length;
                for (int i = 0; i < reveals.Length; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = reveals[i];
                so.ApplyModifiedPropertiesWithoutUndo();

                List<Renderer> bodyRenderers = EnsureBodyCanFade(root);
                WireRoleTint(root, bodyRenderers);

                int bodies = 0;
                foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (r.GetComponentInParent<MembraneReveal>(true) == null) bodies++;
                }
                PrefabUtility.SaveAsPrefabAsset(root, k_AvatarPrefab);
                Debug.Log($"[Formation] on {System.IO.Path.GetFileName(k_AvatarPrefab)}: " +
                          $"{films} film(s), {added} newly given a MembraneReveal, " +
                          $"{reveals.Length} wired into the formation, " +
                          $"{bodies} body renderer(s) it will fade." +
                          "\n[Formation] RESULT: PASS");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        const string k_DitherShader = "Assets/VRMPAssets/Shaders/SimpleLitDither.shadergraph";
        const string k_BodyDitherMaterial = "Assets/Materials/MembraneAvatarBodyDither.mat";

        /// <summary>
        /// Give the Ch36 body a material the fade can actually drive, and return how many
        /// renderers moved.
        ///
        /// AvatarFormation ramps the dither shader's _MaxDistance, which is the mechanism the
        /// project already uses to keep a player from seeing inside their own head. But Ch36's
        /// material is plain URP/Lit and has no such property, so there was nothing to ramp and
        /// the body could only be switched on and off. XRAvatarVisuals' local-player material
        /// swap does not cover it either -- that one handles the head, the HMD and the hands.
        ///
        /// So the body gets a dither material of its own, built from the authored one: the same
        /// base colour, on SimpleLitDither, with the distance band collapsed to zero so a body
        /// at rest is solid at any range and only the fade opens the band.
        /// </summary>
        static List<Renderer> EnsureBodyCanFade(GameObject root)
        {
            var moved = new List<Renderer>();

            var dither = AssetDatabase.LoadAssetAtPath<Shader>(k_DitherShader);
            if (dither == null)
            {
                Debug.LogWarning($"[Formation] no dither shader at {k_DitherShader}; the body " +
                                 "will cut rather than fade.");
                return moved;
            }

            // Everything XRAvatarVisuals already swaps for the local player -- head, HMD, hands.
            // Those land on the project's own dither materials at spawn and can already fade, and
            // they run custom hand shadergraphs that this has no business replacing.
            var managed = CollectSwappedRenderers(root);
            int converted = 0;

            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.GetComponentInParent<MembraneReveal>(true) != null) continue;
                if (managed.Contains(r)) continue;

                Material source = r.sharedMaterial;
                if (source == null) continue;

                // Already fadeable -- converted by an earlier run, or authored that way. Still a
                // body renderer, so it still belongs in the list the tint is wired to. Returning
                // only the ones converted THIS run made the whole thing work once and then
                // quietly stop on the second run.
                if (source.shader == dither || source.HasProperty("_MaxDistance"))
                {
                    moved.Add(r);
                    continue;
                }

                Material variant = LoadOrCreateDither(source, dither);
                if (variant == null) continue;

                r.sharedMaterial = variant;
                converted++;
                moved.Add(r);
            }

            if (converted > 0)
                Debug.Log($"[Formation] moved {converted} body renderer(s) onto a dither " +
                          "material so they can fade rather than cut.");
            return moved;
        }

        /// <summary>
        /// Put the 代表色 on the body: Player A purple, Player B yellow.
        ///
        /// Given the same renderers the dither pass moved, which is exactly the body -- not the
        /// films, and not the head and hands that XRAvatarVisuals owns. Those renderers are
        /// wired in explicitly rather than searched for at runtime, so what gets tinted is
        /// visible in the inspector instead of being decided again on every spawn.
        /// </summary>
        static void WireRoleTint(GameObject root, List<Renderer> bodies)
        {
            if (bodies.Count == 0) return;

            var tint = root.GetComponent<RoleBodyTint>();
            if (tint == null) tint = root.AddComponent<RoleBodyTint>();

            var so = new SerializedObject(tint);
            SerializedProperty list = so.FindProperty("m_Renderers");
            list.arraySize = bodies.Count;
            for (int i = 0; i < bodies.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = bodies[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[Formation] RoleBodyTint wired to {bodies.Count} body renderer(s).");
        }

        /// <summary>
        /// A dither material per SOURCE material, not one for the whole avatar.
        ///
        /// The first version of this made a single material and assigned it to every body
        /// renderer, which collapsed the avatar onto one colour. Each source keeps its own.
        /// </summary>
        static Material LoadOrCreateDither(Material source, Shader dither)
        {
            string path = $"Assets/Materials/{source.name}Dither.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var variant = new Material(dither) { name = $"{source.name}Dither" };
            if (source.HasProperty("_BaseColor") && variant.HasProperty("_BaseColor"))
                variant.SetColor("_BaseColor", source.GetColor("_BaseColor"));
            if (source.HasProperty("_BaseMap") && variant.HasProperty("_BaseMap"))
                variant.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));

            // Band collapsed: nothing is dithered until the fade pushes _MaxDistance out.
            variant.SetFloat("_MinDistance", 0f);
            variant.SetFloat("_MaxDistance", 0f);

            AssetDatabase.CreateAsset(variant, path);
            Debug.Log($"[Formation] created {path} from '{source.name}'.");
            return variant;
        }

        /// <summary>
        /// The renderers XRAvatarVisuals' LocalPlayerMaterialSwap already owns, read off the
        /// serialized field rather than guessed from names. Reflection-free: SerializedObject
        /// walks the nested class without this assembly needing to reference the type.
        /// </summary>
        static HashSet<Renderer> CollectSwappedRenderers(GameObject root)
        {
            var managed = new HashSet<Renderer>();

            foreach (MonoBehaviour mono in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mono == null || mono.GetType().Name != "XRAvatarVisuals") continue;

                var so = new SerializedObject(mono);
                SerializedProperty swap = so.FindProperty("m_LocalPlayerMaterialSwap");
                if (swap == null) continue;

                SerializedProperty end = swap.GetEndProperty();
                SerializedProperty it = swap.Copy();
                while (it.NextVisible(true) && !SerializedProperty.EqualContents(it, end))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (it.objectReferenceValue is Renderer renderer) managed.Add(renderer);
                }
            }

            if (managed.Count > 0)
                Debug.Log($"[Formation] leaving {managed.Count} renderer(s) alone; " +
                          "XRAvatarVisuals already swaps those for the local player.");
            return managed;
        }

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

                // Each player sees only their own 真人. The view component cannot live on the
                // station it hides -- deactivating the object a component is on stops it being
                // told the role changed -- so each gets a sibling under Interaction, which stays
                // active throughout.
                var roles = new[] { ("Human A", PlayerRole.Player1), ("Human B", PlayerRole.Player2) };
                foreach ((string station, PlayerRole role) in roles)
                {
                    Transform target = content.Find(station);
                    if (target == null) continue;

                    Transform holder = Ensure(interaction, $"RoleView {station}");
                    var view = holder.GetComponent<O0FigureRoleView>();
                    if (view == null) view = holder.gameObject.AddComponent<O0FigureRoleView>();

                    var vso = new SerializedObject(view);
                    vso.FindProperty("m_VisibleTo").enumValueIndex = (int)role;
                    vso.FindProperty("m_Station").objectReferenceValue = target.gameObject;
                    vso.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log($"[Touch] {station} is visible to {role} only.");
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
