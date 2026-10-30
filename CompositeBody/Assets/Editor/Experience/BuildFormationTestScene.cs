using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using CompositeBody.Avatar.Skin;
using CompositeBody.Diagnostics;
using CompositeBody.Experience;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// A rehearsal room for the arrival of the player's own body: the membrane growing in, and
    /// the Ch36 body fading up behind it once the film has finished.
    ///
    /// THE AVATAR IS STRIPPED OF ITS NETWORKING, not reproduced without it. The prefab is
    /// instantiated, unpacked, and every NetworkBehaviour and NetworkObject is removed, which
    /// leaves the real rig, the real films, the real materials and the real AvatarFormation --
    /// the thing being judged -- running with no session, no host and no second machine. A scene
    /// built out of stand-in meshes would look like the avatar right up until the moment the
    /// difference mattered.
    ///
    /// Desktop camera, no XR rig. The point is to watch the timing on a monitor and change a
    /// number; putting a headset on to do that is the cost this scene exists to remove.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildFormationTestScene.Run
    /// </summary>
    public static class BuildFormationTestScene
    {
        const string k_ScenePath = "Assets/_Scenes/_Test/AvatarFormationTest.unity";
        const string k_AvatarPath = "Assets/VRMPAssets/Prefabs/PlayerPrefabs/XRI_Network_Player_Avatar.prefab";

        [MenuItem("Tools/O-0/Build Formation Test Scene")]
        public static void Run()
        {
            Debug.Log("[FormationScene] Starting...");

            // Idempotent, and it is what puts a MembraneReveal on the hand and head films. A
            // scene built before that ran would show the hands already wrapped and look like the
            // bug it was built to prove gone.
            BuildO0Touch.AddAvatarFormation();

            var avatarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_AvatarPath);
            if (avatarPrefab == null)
            {
                Debug.LogError($"[FormationScene] RESULT: FAIL - no avatar prefab at {k_AvatarPath}.");
                return;
            }

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Debug.LogError("[FormationScene] RESULT: FAIL - URP/Lit not found.");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildRoom(lit);
            GameObject avatar = BuildAvatar(avatarPrefab);
            if (avatar == null) return;

            var formation = avatar.GetComponentInChildren<AvatarFormation>(true);
            if (formation == null)
            {
                Debug.LogError("[FormationScene] RESULT: FAIL - the avatar has no AvatarFormation.");
                return;
            }

            var rig = new GameObject("Rehearsal");
            rig.AddComponent<FormationRehearsal>();

            Directory.CreateDirectory(Path.GetDirectoryName(k_ScenePath));
            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), k_ScenePath);
            AssetDatabase.SaveAssets();

            var films = avatar.GetComponentsInChildren<MembraneReveal>(true);
            var bodies = new List<string>();
            foreach (var r in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.GetComponentInParent<MembraneReveal>(true) != null) continue;
                bool fades = r.sharedMaterial != null && r.sharedMaterial.HasProperty("_MaxDistance");
                bodies.Add($"{r.name}{(fades ? "" : " (cuts, no _MaxDistance)")}");
            }

            Debug.Log($"[FormationScene] {k_ScenePath}\n" +
                      $"  {films.Length} membrane film(s) under the formation\n" +
                      $"  body renderer(s): {string.Join(", ", bodies)}\n" +
                      "  Press Play. Space touches, R goes back to unformed, L loops.\n" +
                      "[FormationScene] RESULT: PASS");
        }

        static void BuildRoom(Shader lit)
        {
            var root = new GameObject("Room");

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(root.transform, false);
            floor.transform.localScale = new Vector3(0.6f, 1f, 0.6f);   // 6 m square
            var floorMat = new Material(lit) { name = "RehearsalFloor" };
            floorMat.SetColor("_BaseColor", new Color(0.14f, 0.14f, 0.16f));
            floorMat.SetFloat("_Smoothness", 0.1f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;

            var keyGO = new GameObject("Key Light");
            keyGO.transform.SetParent(root.transform, false);
            var key = keyGO.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.1f;
            key.color = new Color(0.97f, 0.97f, 1f);
            key.shadows = LightShadows.Soft;
            keyGO.transform.rotation = Quaternion.Euler(38f, 24f, 0f);

            // The film is nearly all specular and translucent, so in flat light it reads as
            // nothing at all. A cooler light from behind gives it an edge, which is the whole
            // difference between seeing the membrane form and seeing the body brighten.
            var rimGO = new GameObject("Rim Light");
            rimGO.transform.SetParent(root.transform, false);
            var rim = rimGO.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = 0.8f;
            rim.color = new Color(0.60f, 0.72f, 0.95f);
            rim.shadows = LightShadows.None;
            rimGO.transform.rotation = Quaternion.Euler(12f, 208f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.22f, 0.25f, 0.31f);
            RenderSettings.ambientEquatorColor = new Color(0.16f, 0.17f, 0.20f);
            RenderSettings.ambientGroundColor = new Color(0.07f, 0.07f, 0.08f);
            RenderSettings.fog = false;

            var camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            cam.nearClipPlane = 0.05f;
            camGO.AddComponent<UniversalAdditionalCameraData>();

            // Far enough back that the whole figure is in frame, and low enough to read as a
            // person standing rather than as a model on a turntable.
            camGO.transform.position = new Vector3(0.35f, 1.35f, 2.4f);
            camGO.transform.LookAt(new Vector3(0f, 1.0f, 0f));
        }

        static GameObject BuildAvatar(GameObject prefab)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null)
            {
                Debug.LogError("[FormationScene] RESULT: FAIL - could not instantiate the avatar.");
                return null;
            }

            instance.name = "Avatar (networking stripped)";
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));

            // Unpacked first: with the instance still linked, every removal below would be
            // recorded as a prefab override, and the next person to press Revert would put the
            // networking back and wonder why the scene started throwing.
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);

            int stripped = 0;
            foreach (var behaviour in instance.GetComponentsInChildren<NetworkBehaviour>(true))
            {
                Object.DestroyImmediate(behaviour);
                stripped++;
            }
            foreach (var netObj in instance.GetComponentsInChildren<NetworkObject>(true))
            {
                Object.DestroyImmediate(netObj);
                stripped++;
            }

            // Left in place but switched off. It swaps the local player onto dither materials and
            // hides the head so nobody looks inside their own skull -- correct in the show, and
            // the opposite of what a scene built to look at the body from outside wants.
            int disabled = 0;
            foreach (var mono in instance.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mono == null) continue;
                if (mono.GetType().Name != "XRAvatarVisuals") continue;
                mono.enabled = false;
                disabled++;
            }

            Debug.Log($"[FormationScene] avatar: {stripped} network component(s) removed, " +
                      $"{disabled} visuals component(s) switched off.");
            return instance;
        }
    }
}
