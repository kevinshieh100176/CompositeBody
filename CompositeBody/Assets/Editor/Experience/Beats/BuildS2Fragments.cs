using System.IO;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using XRMultiplayer;
using CompositeBody.Assembly.EditorTools;
using CompositeBody.Experience;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// S2-2 碎片：燈 -- the SmallProps lamp cut in two, one half for each player, wired into
    /// MainScene so that 「兩個人都抓過之後播一段 VO，兩個碎片靠近就自己拼起來，然後跑到 playspace 正中央」.
    ///
    /// WHY THE FRAGMENTS ARE NOT UNDER Beat_S2-2. Every other beat's content lives under the
    /// beat root the Main timeline's Control clip activates, and these cannot: they are
    /// NetworkObjects placed in the scene, and Netcode collects those ONCE, during
    /// synchronization, from what is active -- an object under a root that comes up twenty
    /// minutes into the show is never spawned, so the server would have no halves to weld and
    /// neither player would see the other's. So they sit under an always-active root beside the
    /// beats, and <see cref="CompositeFragmentPair"/> keeps them invisible and ungrabbable until
    /// Beat_S2-2 comes up. The beat root is still what says when; it is simply read rather than
    /// lived in.
    ///
    /// AND THE HALVES ARE FROZEN UNTIL SOMEBODY TAKES ONE. NetworkPhysicsInteractable's
    /// spawnLocked freezes the body on spawn and releases it on the first grab, which is the
    /// same fix O1GrabStation had to write by hand: a body with gravity off has no forces on it
    /// and therefore keeps any push it is ever given, so a knee catching an invisible fragment
    /// on the way past used to send it across the room at walking pace with nothing to stop it.
    ///
    /// Re-runnable: it replaces the halves it finds under its own root and leaves everything
    /// else in the scene alone.
    ///
    /// OPENS MainScene, so save whatever is open first.
    /// </summary>
    public static class BuildS2Fragments
    {
        const string k_MainScene = "Assets/_Scenes/MainScene.unity";
        const string k_GhostMaterialPath = "Assets/Materials/GhostHalf.mat";
        const string k_LinePath = "Assets/_Audio/old/S2-2.wav";

        const string k_BeatsRoot = "Beats";
        const string k_BeatRoot = "Beat_S2-2";
        const string k_FragmentsRoot = "S2-2 Fragments";

        /// <summary>Shared by both halves, and the key the pair finds itself by at runtime.</summary>
        public const string PairId = "s2_2_lamp";

        /// <summary>
        /// 「兩個人都抓過」 and 「拼好了」. Keys, not descriptions: renaming one mid-run strands the
        /// flag on both machines, so these are as dull as possible on purpose.
        /// </summary>
        public const string PickedUpTask = "s2_2.lamp.held";
        public const string AssembledTask = "s2_2.lamp.assembled";

        /// <summary>
        /// The lamp at the size O-1 already shows it at. The same object the players handled in
        /// the onboarding, which is the point of it being this one.
        /// </summary>
        const float k_LampMetres = 0.40f;

        /// <summary>
        /// How close the halves have to come, AS A FRACTION OF THE SEAM they close -- not a
        /// number in metres.
        ///
        /// A FIXED RADIUS MEANS SOMETHING DIFFERENT FOR EVERY PROP, and for this one it meant
        /// something absurd. The chair's 0.12 m is about half the gap between its two pivots;
        /// the lamp's pivots are 0.078 m apart, so the same 0.12 m is wider than the assembled
        /// lamp -- the halves would have snapped together from clear across the gap, while the
        /// player was still reaching, which looks like the prop being yanked out of their hand.
        /// Six tenths of the seam is "nearly touching" for anything, which is what the beat
        /// actually asks for.
        /// </summary>
        const float k_SnapFraction = 0.6f;

        /// <summary>
        /// Floor and ceiling on that, because a fraction of a very small or very large prop is
        /// not reachable in a headset either way.
        /// </summary>
        const float k_MinSnap = 0.03f, k_MaxSnap = 0.12f;

        /// <summary>
        /// Millimetres off the exact midline, so the cut severs real geometry and both faces are
        /// capped. See <see cref="PropHalves.Spec.axisOffset"/> -- at exactly 0 this lamp comes
        /// apart along its own mirror seam and both halves are hollow from the cut side.
        /// </summary>
        const float k_CutOffset = 0.004f;

        /// <summary>
        /// Where each half hangs, in the playspace's own space: chest height, half a metre
        /// forward, one to each side. Hanging rather than standing on the floor because they are
        /// 碎片 and because a fragment at chest height is already where a hand expects it.
        /// </summary>
        static readonly Vector3 k_HomeA = new(-0.70f, 1.05f, 0.55f);
        static readonly Vector3 k_HomeB = new(0.70f, 1.05f, 0.55f);

        [MenuItem("Tools/Show/Build S2-2 Fragments")]
        static void RunFromMenu() => Run();

        public static void Run()
        {
            Scene scene = EditorSceneManager.OpenScene(k_MainScene, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[S2-2] RESULT: FAIL - could not open {k_MainScene}.");
                return;
            }

            GameObject beats = Find(scene, k_BeatsRoot);
            if (beats == null)
            {
                Debug.LogError($"[S2-2] RESULT: FAIL - no '{k_BeatsRoot}' root in MainScene.");
                return;
            }

            GameObject beat = Find(scene, k_BeatRoot);
            if (beat == null)
            {
                Debug.LogError($"[S2-2] RESULT: FAIL - no '{k_BeatRoot}' in MainScene, so there " +
                               "is nothing to tell the fragments when the beat is up.");
                return;
            }

            PropHalves.Cut lamp = PropHalves.Build(new PropHalves.Spec
            {
                modelPath = "Assets/_models/SmallProps/Lamp.fbx",
                bodyMaterialPath = "Assets/_models/SmallProps/Lamp.mat",
                sizeMetres = k_LampMetres,
                meshDir = "Assets/_models/SmallProps/Halves",
                name = "Lamp",
                cutFaceMaterialPath = "Assets/Materials/LampCutFace.mat",
                axisOffset = k_CutOffset,
            });

            if (lamp == null)
            {
                Debug.LogError("[S2-2] RESULT: FAIL - the lamp could not be cut.");
                return;
            }

            Material ghost = LoadOrCreateGhostMaterial();
            if (ghost == null) return;

            Transform playspace = FindPlayspace();
            Vector3 homeA = ToWorld(playspace, k_HomeA);
            Vector3 homeB = ToWorld(playspace, k_HomeB);

            GameObject root = FindOrCreateRoot(scene, beats.transform);

            GameObject halfA = BuildHalf(root.transform, "LampHalf (Player A)", lamp.negative,
                                         lamp.materials, homeA);
            GameObject halfB = BuildHalf(root.transform, "LampHalf (Player B)", lamp.positive,
                                         lamp.materials, homeB);

            var socket = new GameObject("AssemblySocket").transform;
            socket.SetParent(halfA.transform, false);

            // Straight from the geometry: the gap between the two pivots is whatever the cut
            // made it, never a number to tune.
            socket.localPosition = lamp.socketOffset;

            float snap = Mathf.Clamp(lamp.socketOffset.magnitude * k_SnapFraction,
                                     k_MinSnap, k_MaxSnap);

            CompositeHalf anchor = Configure(halfA, PlayerRole.Player1, true, socket, ghost, snap);
            CompositeHalf follower = Configure(halfB, PlayerRole.Player2, false, null, ghost, snap);

            WirePair(root, anchor, follower, beat);

            // The lamp as the players will see it assembled, before the scene is saved: the cut
            // is only correct if the two halves put back together are the lamp again.
            Debug.Log($"[S2-2] Socket offset {lamp.socketOffset:F4}, so the halves sit " +
                      $"{lamp.socketOffset.magnitude:0.000} m apart when welded.");

            // Scene-placed NetworkObjects derive their hash from where they sit, and a hash of 0
            // -- which is what a freshly added one carries until something revalidates it --
            // can never be matched between host and client. See BuildMainScene.
            RegenerateNetworkObjectHashes();

            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Verify(root, anchor, follower, socket, lamp);
        }

        #region Building

        static GameObject FindOrCreateRoot(Scene scene, Transform beats)
        {
            GameObject root = Find(scene, k_FragmentsRoot);
            if (root == null)
            {
                root = new GameObject(k_FragmentsRoot);
                root.transform.SetParent(beats, false);
                Debug.Log($"[S2-2] Made '{k_FragmentsRoot}' under '{k_BeatsRoot}'.");
            }

            // ACTIVE, ALWAYS, and said out loud because the whole arrangement depends on it: a
            // root switched off here is a pair of halves Netcode never spawns.
            root.SetActive(true);

            // Replaced rather than added to, so a re-run after a re-cut does not leave the old
            // halves in the room beside the new ones.
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = root.transform.GetChild(i);
                if (child.GetComponent<CompositeHalf>() != null) Object.DestroyImmediate(child.gameObject);
            }

            return root;
        }

        static GameObject BuildHalf(Transform parent, string name, Mesh mesh, Material[] materials,
                                    Vector3 position)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;

            // One slot per submesh: the body takes the lamp's own material, the cut face takes
            // the raw inside, so which edge to bring to the other half is obvious across a room.
            go.GetComponent<MeshRenderer>().sharedMaterials = mesh.subMeshCount > 1
                ? materials
                : new[] { materials[0] };

            // Convex, because a Rigidbody that is not kinematic cannot carry a concave collider.
            // The hull is fatter than the lamp -- it fills in under the shade -- but it stops
            // dead at the cut plane, no vertex of this half lying past it, so the two hulls meet
            // exactly where the halves do and cannot hold each other apart short of the snap.
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = true;

            return go;
        }

        static CompositeHalf Configure(GameObject go, PlayerRole role, bool isAnchor,
                                       Transform socket, Material ghostMat, float snapRadius)
        {
            // Order matters: each of these has RequireComponent dependencies on the ones before
            // it, and letting Unity auto-add them produces default-configured components that
            // are then hard to tell apart from ones somebody authored.
            go.AddComponent<NetworkObject>();

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.8f;

            // Hanging, not falling: these are fragments of a memory, not objects on a table. The
            // first grab is also the last time gravity is asked about -- forceGravityOnDetach
            // stays off below, so a half put down stays where it was put.
            rb.useGravity = false;
            rb.isKinematic = false;
            rb.linearDamping = 1.5f;
            rb.angularDamping = 1f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            go.AddComponent<ClientNetworkTransform>();

            var grab = go.AddComponent<XRGrabInteractable>();
            grab.useDynamicAttach = true;        // held where the hand closed, not snapped to the pivot
            grab.throwOnDetach = false;          // meant to be brought together, not thrown
            grab.forceGravityOnDetach = false;   // and to stay where they are let go of

            // spawnLocked freezes the body until the first grab, which is what stops an
            // invisible fragment being knocked across the room during the twenty minutes before
            // this beat. It also hands ownership to whoever grabs it, which is what lets their
            // ClientNetworkTransform write the pose the other machine reads.
            var physics = go.AddComponent<NetworkPhysicsInteractable>();
            physics.spawnLocked = true;

            // RoleGhost before CompositeHalf: it caches the solid materials the first time it is
            // asked to swap, and has to see the real ones rather than the ghost.
            var roleGhost = go.AddComponent<RoleGhost>();
            var ghostSo = new SerializedObject(roleGhost);
            ghostSo.FindProperty("m_GhostMaterial").objectReferenceValue = ghostMat;
            SerializedProperty solidFor = ghostSo.FindProperty("m_SolidForRoles");
            solidFor.arraySize = 1;
            solidFor.GetArrayElementAtIndex(0).intValue = (int)role;
            ghostSo.ApplyModifiedPropertiesWithoutUndo();

            var half = go.AddComponent<CompositeHalf>();
            var so = new SerializedObject(half);
            so.FindProperty("m_PairId").stringValue = PairId;
            so.FindProperty("m_OwnedBy").intValue = (int)role;
            so.FindProperty("m_IsAnchor").boolValue = isAnchor;
            so.FindProperty("m_SnapRadius").floatValue = snapRadius;
            so.FindProperty("m_Ghost").objectReferenceValue = roleGhost;
            so.FindProperty("m_Interactable").objectReferenceValue = grab;
            so.FindProperty("m_Rigidbody").objectReferenceValue = rb;

            if (socket != null) so.FindProperty("m_AssemblySocket").objectReferenceValue = socket;

            if (isAnchor)
            {
                // Both gates and the snap rule are the anchor's, because the anchor is the half
                // that runs the merge check. The follower carries neither, and CompositeHalf
                // reads them off its partner.
                so.FindProperty("m_CompletedTaskId").stringValue = AssembledTask;
                so.FindProperty("m_PickedUpTaskId").stringValue = PickedUpTask;

                // 「當兩個碎片靠近到某個距離內的時候兩個碎片會自己拼在一起」 -- proximity, once both
                // players have had theirs. Still two people's work, because the latch is per
                // role and nobody can set another player's; but one of them may put their half
                // down to line the other one up, which is what hands actually do.
                so.FindProperty("m_SnapNeeds").enumValueIndex = (int)CompositeHalf.SnapCondition.BothHavePickedItUp;
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[S2-2] {go.name}: owned by {role}, anchor={isAnchor}, snap {snapRadius:0.000} m.");
            return half;
        }

        static void WirePair(GameObject root, CompositeHalf anchor, CompositeHalf follower,
                             GameObject beat)
        {
            var pair = root.GetComponent<CompositeFragmentPair>();
            if (pair == null) pair = root.AddComponent<CompositeFragmentPair>();

            var line = AssetDatabase.LoadAssetAtPath<AudioClip>(k_LinePath);
            if (line == null)
                Debug.LogWarning($"[S2-2] No line at {k_LinePath}; the beat will assemble in " +
                                 "silence.");

            var source = root.GetComponent<AudioSource>();
            if (source == null) source = root.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;   // a line in your ear, not a point in the room
            source.clip = line;

            var so = new SerializedObject(pair);
            so.FindProperty("m_Anchor").objectReferenceValue = anchor;
            so.FindProperty("m_Follower").objectReferenceValue = follower;
            so.FindProperty("m_BeatRoot").objectReferenceValue = beat;
            so.FindProperty("m_Line").objectReferenceValue = line;
            so.FindProperty("m_Source").objectReferenceValue = source;

            // Left empty so the destination is read off the Playspace live. The room is a thing
            // the show moves; a Transform captured here would be the middle of where the room
            // used to be.
            so.FindProperty("m_Destination").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[S2-2] Pair wired: line " +
                      $"{(line != null ? $"{line.name} ({line.length:0.0}s)" : "none")}, " +
                      $"armed by '{beat.name}'.");
        }

        #endregion

        #region Assets and lookups

        static Material LoadOrCreateGhostMaterial()
        {
            var shader = Shader.Find("CompositeBody/GhostHalf");
            if (shader == null)
            {
                Debug.LogError("[S2-2] RESULT: FAIL - shader 'CompositeBody/GhostHalf' not found.");
                return null;
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(k_GhostMaterialPath);
            if (mat == null)
            {
                Directory.CreateDirectory("Assets/Materials");
                mat = new Material(shader) { name = "GhostHalf" };
                AssetDatabase.CreateAsset(mat, k_GhostMaterialPath);

                // The settings the combine scene dialled in: cool and cold against a warm prop,
                // so which half is yours is legible at a glance rather than something to work
                // out. Only written for a material this made, so a tuned one is left alone.
                mat.SetColor("_GhostColor", new Color(0.42f, 0.72f, 1f));
                mat.SetFloat("_FillAlpha", 0.05f);
                mat.SetFloat("_RimPower", 4f);
                mat.SetFloat("_RimIntensity", 1.5f);
                mat.SetFloat("_EdgeAlpha", 0.9f);
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                EditorUtility.SetDirty(mat);
            }

            return mat;
        }

        static Transform FindPlayspace()
        {
            var playspace = Object.FindAnyObjectByType<Playspace>(FindObjectsInactive.Include);
            if (playspace != null) return playspace.transform;

            Debug.LogWarning("[S2-2] No Playspace in MainScene, so the fragments are placed " +
                             "against the world origin. That is where the piece assumes the " +
                             "middle of the room is, so this is usually the same answer.");
            return null;
        }

        static Vector3 ToWorld(Transform playspace, Vector3 local) =>
            playspace != null ? playspace.TransformPoint(local) : local;

        static GameObject Find(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root;
                Transform found = FindDeep(root.transform, name);
                if (found != null) return found.gameObject;
            }
            return null;
        }

        static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        static void RegenerateNetworkObjectHashes()
        {
            MethodInfo onValidate = typeof(NetworkObject).GetMethod(
                "OnValidate", BindingFlags.NonPublic | BindingFlags.Instance);

            if (onValidate == null)
            {
                Debug.LogError("[S2-2] Could not find NetworkObject.OnValidate; hashes may stay 0.");
                return;
            }

            foreach (NetworkObject netObj in Object.FindObjectsByType<NetworkObject>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                onValidate.Invoke(netObj, null);
                EditorUtility.SetDirty(netObj);
            }
        }

        static uint HashOf(NetworkObject netObj) =>
            new SerializedObject(netObj).FindProperty("GlobalObjectIdHash")?.uintValue ?? 0u;

        #endregion

        #region Verify

        static void Verify(GameObject root, CompositeHalf anchor, CompositeHalf follower,
                           Transform socket, PropHalves.Cut lamp)
        {
            bool ok = true;

            ok &= Check(root.activeInHierarchy,
                        "the fragments' root is active, so Netcode will spawn the halves",
                        "the fragments' root is NOT active -- Netcode collects in-scene " +
                        "NetworkObjects from what is active, so neither half would ever spawn");

            // A HALF WITH NO CAP IS SEE-THROUGH FROM THE CUT SIDE, which is the side the player
            // holding it looks at for the whole beat. It is also the failure the midline cut
            // produces silently -- see PropHalves.Spec.axisOffset -- so it is checked rather
            // than trusted.
            ok &= Check(lamp.negative.subMeshCount > 1 && lamp.positive.subMeshCount > 1,
                        "both halves are capped at the cut",
                        "one or both halves came back with no cap, so they are open shells and " +
                        "a player can see through the cut face. Move the plane off the midline " +
                        "with Spec.axisOffset until the split reports clipped triangles");

            foreach (CompositeHalf half in new[] { anchor, follower })
            {
                ok &= Check(half.GetComponent<ClientNetworkTransform>() != null,
                            $"{half.name} replicates its pose",
                            $"{half.name} has no ClientNetworkTransform, so the other machine " +
                            "never sees it move and the halves can never be judged close");

                ok &= Check(half.GetComponent<XRGrabInteractable>() != null,
                            $"{half.name} can be picked up",
                            $"{half.name} has no XRGrabInteractable");

                ok &= Check(half.GetComponent<RoleGhost>() != null,
                            $"{half.name} is ghosted for the other player",
                            $"{half.name} has no RoleGhost, so both players see it solid and " +
                            "both can grab it");

                uint hash = HashOf(half.GetComponent<NetworkObject>());
                ok &= Check(hash != 0u,
                            $"{half.name} has network hash {hash}",
                            $"{half.name} has hash 0 and can never be matched between machines");
            }

            // THE PAIR HAS TO REASSEMBLE INTO THE LAMP. Both halves put back at the socket
            // offset should occupy exactly the box the uncut lamp did -- anything else means the
            // cut and the socket disagree, which in a headset looks like a lamp with a step in
            // it. A re-cut at a different size is the thing that would silently break this.
            Bounds assembled = lamp.negative.bounds;
            Bounds other = lamp.positive.bounds;
            other.center += lamp.socketOffset;
            assembled.Encapsulate(other);

            float seam = (assembled.size - lamp.sourceBounds.size).magnitude;
            ok &= Check(seam < 0.002f,
                        $"the halves reassemble into the lamp ({seam * 1000f:0.0} mm out)",
                        $"the reassembled pair is {seam * 1000f:0.0} mm bigger or smaller than " +
                        "the uncut lamp, so the seam is in the wrong place");

            float socketOut = Vector3.Distance(socket.localPosition, lamp.socketOffset);
            ok &= Check(socketOut < 0.0005f,
                        "the socket sits where the other half's pivot belongs",
                        $"the socket is {socketOut:0.000} m from the socket offset; the halves " +
                        "would weld misaligned");

            var pair = root.GetComponent<CompositeFragmentPair>();
            ok &= Check(pair != null,
                        "the beat's sequence -- line, snap, journey -- is wired",
                        "no CompositeFragmentPair on the root, so nothing speaks and nothing " +
                        "travels");

            Debug.Log(ok
                ? "[S2-2] RESULT: PASS - the lamp is cut, both halves are in MainScene and the " +
                  "beat is wired."
                : "[S2-2] RESULT: FAIL - see the lines above.");
        }

        static bool Check(bool ok, string good, string bad)
        {
            if (ok) Debug.Log($"[S2-2] OK   {good}");
            else Debug.LogError($"[S2-2] FAIL {bad}");
            return ok;
        }

        #endregion
    }
}
