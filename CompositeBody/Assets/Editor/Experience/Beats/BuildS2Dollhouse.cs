using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using XRMultiplayer;
using CompositeBody.Experience;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// S2-3｜隱藏的記憶 -- the low-poly house cut into a dollhouse the two players rebuild in mid-air,
    /// wired into MainScene so that 「碎片靠 Lowpoly_Wall.Floor_small 就吸上去，對應的大碎片從天上飛下來,
    /// 全部拼完兩組 LowPolyWall 都 dissolve out，Inner_Wall dissolve in」.
    ///
    /// WHAT IT READS AND WHAT IT WRITES. The authoring lives in the S2_v1 section: 'LowPolyWall'
    /// is the real house at full size and 'LowPolyWall (small)' is the same five pieces at a
    /// twentieth of it, sitting correctly assembled. Those two groups are the SOURCE -- this copies
    /// their meshes, their materials and, crucially, their poses, and builds the playable beat
    /// beside them. It never edits them. 「正確版屋子之後會補」, so re-running after a corrected house
    /// is dropped in is the whole point: there is not one authored number in here that a new house
    /// would invalidate.
    ///
    /// WHY THE FRAGMENTS ARE NOT UNDER Beat_S2-3. The same reason BuildS2Fragments gives: they are
    /// NetworkObjects placed in the scene, and Netcode collects those ONCE, during synchronization,
    /// from what is active. An object under a root the show brings up half an hour in is never
    /// spawned, so the server would have no fragments to place and neither player would see the
    /// other's. They sit under an always-active root beside the beats and
    /// <see cref="DollhouseAssembly"/> keeps them invisible and ungrabbable until Beat_S2-3 comes up.
    ///
    /// THE SMALL PIECES ARE COPIED, NOT MOVED. The originals stay inactive where they were
    /// authored, because that group IS the answer key -- it is the only record of where each
    /// fragment belongs, and the slots are derived from it every time this runs.
    ///
    /// THE FRAGMENTS DO NOT COLLIDE WITH EACH OTHER. Four wall slabs assembling into a box meet at
    /// the corners, and a placed wall is a frozen body: without this, the third piece would be held
    /// out of its own slot by the two already home, which in a headset looks exactly like the snap
    /// being broken. Collision-based ownership exchange goes off for the same reason -- two
    /// fragments touching must not hand one player's piece to the other.
    ///
    /// OPENS MainScene, so save whatever is open first.
    /// </summary>
    public static class BuildS2Dollhouse
    {
        const string k_MainScene = "Assets/_Scenes/MainScene.unity";

        const string k_BeatsRoot = "Beats";
        const string k_BeatRoot = "Beat_S2-3";
        const string k_Root = "S2-3 Dollhouse";

        /// <summary>The two authored groups this reads, and the real wall it hands the room back to.</summary>
        const string k_SmallGroup = "LowPolyWall (small)";
        const string k_BigGroup = "LowPolyWall";
        const string k_InnerWall = "Inner_Wall";

        /// <summary>
        /// What marks the piece the players are GIVEN rather than asked for. Matched on the name
        /// with the suffix already stripped, so the typo in 'Lowpoly_Wal.Back' cannot be mistaken
        /// for it.
        /// </summary>
        const string k_BaseMarker = "Floor";

        const string k_SmallSuffix = "_small";

        const string k_DissolveDir = "Assets/Materials/S2_3";
        const string k_DissolveShader = "CompositeBody/MemoryFragments";

        /// <summary>
        /// When the house finishes itself, 「播完就補完」 -- and it has to finish INSIDE the beat's own
        /// clip. Beat_S2-3's Control clip on Main.playable runs 997s to 1177s, so the root is up for
        /// 180 seconds; the voice-over it is timed against is 3:10, which is 190. An auto-complete
        /// at 190 would fire ten seconds after the root had already gone dark, leaving the players
        /// looking at an unfinished house that never resolves.
        ///
        /// So this is set from the clip, not from the voice-over: 150 seconds, which leaves thirty
        /// for the four fragments to fly in, the pause, and both halves of the dissolve. Lengthen
        /// the clip to about 205 and this can go to 190.
        /// </summary>
        const float k_AutoCompleteAfter = 150f;

        /// <summary>
        /// Where each fragment waits, 「在 playspace 中央空中」: on a ring around the base, at the
        /// height a standing person's hands are already at. Each piece starts on the side of the
        /// ring its own slot is on, so the left wall is out to the left -- a free-for-all grab
        /// where every piece starts in a scrambled position is a puzzle about finding things
        /// rather than about building something.
        /// </summary>
        /// <summary>
        /// The ring is this much clear of the outermost slot, then held inside
        /// <see cref="k_ScatterMin"/>..<see cref="k_ScatterMax"/> so it stays within reach. A fixed
        /// radius would be wrong twice over: on a small house the fragments would start inside
        /// their own snap radius and go home the moment anybody brushed them, and on a larger one
        /// they would start out of arm's reach.
        /// </summary>
        const float k_ScatterClearance = 0.45f;
        const float k_ScatterMin = 0.6f, k_ScatterMax = 0.8f;

        /// <summary>
        /// How far below and above the base the ring spreads.
        ///
        /// MEASURED FROM THE BASE, NOT FROM THE PLAYSPACE, which is the thing that cost a build.
        /// The sections are laid out far apart in the world and the playspace is carried to each
        /// of them during the show, so its authored position is wherever the players START, not
        /// where they will be standing in S2-3 -- taking a floor height from it hung this house's
        /// fragments twenty-one metres under their own slots. The base is the only thing in the
        /// scene that knows where the dollhouse actually is, and it was authored at hand height
        /// within its own section, so a spread around it is hand height wherever the section goes.
        /// </summary>
        const float k_ScatterDrop = 0.25f, k_ScatterRise = 0.15f;

        /// <summary>
        /// How close a fragment has to come, AS A FRACTION OF THE GAP BETWEEN SLOTS rather than a
        /// number in metres -- the lesson BuildS2Fragments paid for. A fixed radius means something
        /// different for every house, and the corrected house is explicitly coming later.
        /// </summary>
        const float k_SnapFraction = 0.35f;

        /// <summary>Floor and ceiling on that, because a fraction of a very small or very large
        /// house is not reachable in a headset either way. These are the 8-12 cm the beat was
        /// specified at.</summary>
        const float k_MinSnap = 0.04f, k_MaxSnap = 0.12f;

        [MenuItem("Tools/Show/Build S2-3 Dollhouse")]
        static void RunFromMenu() => Run();

        public static void Run()
        {
            Scene scene = EditorSceneManager.OpenScene(k_MainScene, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[S2-3] RESULT: FAIL - could not open {k_MainScene}.");
                return;
            }

            GameObject beats = Find(scene, k_BeatsRoot);
            GameObject beat = Find(scene, k_BeatRoot);
            GameObject smallGroup = Find(scene, k_SmallGroup);
            GameObject bigGroup = Find(scene, k_BigGroup);

            if (beats == null || beat == null || smallGroup == null || bigGroup == null)
            {
                Debug.LogError(
                    $"[S2-3] RESULT: FAIL - missing in MainScene: " +
                    $"{(beats == null ? $"'{k_BeatsRoot}' " : "")}" +
                    $"{(beat == null ? $"'{k_BeatRoot}' " : "")}" +
                    $"{(smallGroup == null ? $"'{k_SmallGroup}' " : "")}" +
                    $"{(bigGroup == null ? $"'{k_BigGroup}' " : "")}" +
                    "- nothing can be built without all four.");
                return;
            }

            // Pair the two groups by name, with the suffix off. The typo in 'Lowpoly_Wal.Back' is
            // in both groups, so stripping the suffix matches it to itself and nothing has to know
            // about it.
            var pairs = new List<(Transform small, Transform big)>();
            Transform baseSource = null;
            Transform baseBig = null;

            foreach (Transform small in smallGroup.transform)
            {
                string stem = StripSuffix(small.name);
                Transform big = FindCounterpart(bigGroup.transform, stem);

                if (stem.Contains(k_BaseMarker))
                {
                    baseSource = small;
                    baseBig = big;
                    continue;
                }

                pairs.Add((small, big));
            }

            if (baseSource == null)
            {
                Debug.LogError($"[S2-3] RESULT: FAIL - no piece under '{k_SmallGroup}' whose name " +
                               $"contains '{k_BaseMarker}', so there is no base to assemble onto.");
                return;
            }

            if (pairs.Count == 0)
            {
                Debug.LogError($"[S2-3] RESULT: FAIL - '{k_SmallGroup}' has no pieces besides the " +
                               "base, so there is nothing for the players to do.");
                return;
            }

            Material dissolveShared = DissolveMaterialFor(baseSource.GetComponent<MeshRenderer>(),
                                                          "LowpolyWall");
            if (dissolveShared == null) return;

            GameObject root = FindOrCreateRoot(scene, beats.transform);

            // The base first: every slot is a child of it, and the fragments are scattered around
            // it.
            GameObject baseGo = BuildMesh(root.transform, "Base", baseSource, dissolveShared);
            StripColliders(baseGo);

            // THE BASE HAS NO COLLIDER, on purpose. Nothing needs to collide with it and a convex
            // hull of a floor plate is the one thing that could hold a wall out of its own slot --
            // a fragment passing a few millimetres through the floor on its way home is invisible;
            // a fragment bouncing off it is the beat not working.

            var dollhousePieces = new List<DollhouseAssembly.Piece>();
            var fragments = new List<DollhousePiece>();
            var slots = new List<Transform>();

            // Slots first, all of them, because the snap radius is derived from how far apart they
            // are and no piece can be configured until that is known.
            foreach (var (small, _) in pairs)
            {
                var slot = new GameObject($"Slot ({StripSuffix(small.name)})").transform;
                slot.SetParent(baseGo.transform, false);
                slot.SetPositionAndRotation(small.position, small.rotation);
                slots.Add(slot);
            }

            float snap = SnapRadiusFor(slots);
            float ring = RingRadiusFor(baseGo.transform, slots);

            for (int i = 0; i < pairs.Count; i++)
            {
                (Transform small, Transform big) = pairs[i];

                GameObject fragment = BuildMesh(root.transform, StripSuffix(small.name) + " (fragment)",
                                                small, dissolveShared);

                // Out on the ring, on its own side of the house, with the rotation it will end up
                // with: 「朝向一開始就是正確的」. A fragment that also has to be turned the right way
                // round is a different and much harder puzzle than the one the beat asks for.
                fragment.transform.SetPositionAndRotation(
                    ScatterPose(baseGo.transform, slots[i], i, pairs.Count, ring),
                    small.rotation);

                DollhousePiece piece = Configure(fragment, slots[i], snap);
                fragments.Add(piece);

                dollhousePieces.Add(new DollhouseAssembly.Piece
                {
                    fragment = piece,
                    big = BuildMesh(root.transform, StripSuffix(small.name) + " (wall)", big,
                                    dissolveShared)?.transform,
                });
            }

            // The floor's big counterpart has no fragment: the players are given the base, so its
            // wall comes down as soon as the beat is up.
            if (baseBig != null)
            {
                dollhousePieces.Add(new DollhouseAssembly.Piece
                {
                    fragment = null,
                    big = BuildMesh(root.transform, StripSuffix(baseSource.name) + " (wall)",
                                    baseBig, dissolveShared)?.transform,
                });
            }

            MemoryDissolve lowPoly = BuildLowPolyDissolve(root, dollhousePieces, baseGo);
            MemoryDissolve innerWall = BuildInnerWallDissolve(root, scene);

            WireAssembly(root, baseGo.transform, dollhousePieces, beat, lowPoly, innerWall);

            // Scene-placed NetworkObjects derive their hash from where they sit, and a hash of 0 --
            // what a freshly added one carries until something revalidates it -- can never be
            // matched between host and client. See BuildMainScene.
            RegenerateNetworkObjectHashes();

            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Verify(root, baseGo, fragments, dollhousePieces, slots, snap, innerWall);
        }

        #region Building

        static string StripSuffix(string name) =>
            name.EndsWith(k_SmallSuffix) ? name[..^k_SmallSuffix.Length] : name;

        /// <summary>
        /// The big wall that answers one small fragment, by name -- and by the end of the name if
        /// the two groups do not agree on the whole of it.
        ///
        /// THE TWO GROUPS DO NOT ALWAYS SPELL A PIECE THE SAME WAY. 'Lowpoly_Wal.Back' against
        /// 'Lowpoly_Wall.Back' is one missing letter that was fixed in the small group and not in
        /// the big one, and an exact match alone turns that into a fragment the players can place
        /// with no wall to answer it -- a silent hole in the middle of the beat. The part after the
        /// last dot is unambiguous inside a five-piece group, so it is worth falling back to, and
        /// worth saying out loud so somebody fixes the name.
        /// </summary>
        static Transform FindCounterpart(Transform group, string stem)
        {
            Transform exact = group.Find(stem);
            if (exact != null) return exact;

            string tail = Tail(stem);
            foreach (Transform child in group)
            {
                if (Tail(child.name) != tail) continue;

                Debug.LogWarning($"[S2-3] '{k_BigGroup}' has no '{stem}'; matched '{child.name}' on " +
                                 $"'{tail}' instead. The two groups spell this piece differently -- " +
                                 "worth renaming one of them.");
                return child;
            }

            Debug.LogWarning($"[S2-3] Nothing under '{k_BigGroup}' answers '{stem}', so no wall " +
                             "will come down for it.");
            return null;
        }

        static string Tail(string name)
        {
            int dot = name.LastIndexOf('.');
            return dot >= 0 ? name[(dot + 1)..] : name;
        }

        static GameObject FindOrCreateRoot(Scene scene, Transform beats)
        {
            GameObject root = Find(scene, k_Root);
            if (root == null)
            {
                root = new GameObject(k_Root);
                root.transform.SetParent(beats, false);
                Debug.Log($"[S2-3] Made '{k_Root}' under '{k_BeatsRoot}'.");
            }

            // ACTIVE, ALWAYS, and said out loud because the whole arrangement depends on it: a root
            // switched off here is a set of fragments Netcode never spawns.
            root.SetActive(true);
            root.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            // Rebuilt rather than added to, so a re-run after a corrected house does not leave the
            // old fragments in the room beside the new ones.
            for (int i = root.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

            return root;
        }

        /// <summary>
        /// A copy of one authored piece at its authored place: same mesh, same world pose, same
        /// effective scale. The scale is taken from the source's lossy scale because the authored
        /// pieces sit under a group scaled to a twentieth and this root is not, so matching the
        /// local scale would build a house twenty times too big.
        /// </summary>
        static GameObject BuildMesh(Transform parent, string name, Transform source, Material dissolve)
        {
            if (source == null) return null;

            var filter = source.GetComponent<MeshFilter>();
            var renderer = source.GetComponent<MeshRenderer>();
            if (filter == null || filter.sharedMesh == null || renderer == null)
            {
                Debug.LogWarning($"[S2-3] '{source.name}' has no mesh to copy, so '{name}' was not " +
                                 "built.");
                return null;
            }

            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(source.position, source.rotation);
            go.transform.localScale = source.lossyScale;

            go.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;

            // One dissolving material per slot, so a piece with several submeshes still goes
            // entirely rather than in part.
            var mats = new Material[renderer.sharedMaterials.Length == 0
                                        ? 1
                                        : renderer.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = dissolve;
            go.GetComponent<MeshRenderer>().sharedMaterials = mats;

            // Lightmaps belong to the authored copy, not to this one: these pieces move, and a
            // moving object carrying an inherited lightmap index samples somebody else's baked
            // light -- which on this house would be a wall lit as though it were somewhere else.
            var built = go.GetComponent<MeshRenderer>();
            built.lightmapIndex = -1;
            built.shadowCastingMode = renderer.shadowCastingMode;

            return go;
        }

        static void StripColliders(GameObject go)
        {
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(c);
        }

        /// <summary>
        /// Everything one grabbable fragment needs. Order matters: each of these has
        /// RequireComponent dependencies on the ones before it, and letting Unity auto-add them
        /// produces default-configured components that are then hard to tell apart from ones
        /// somebody authored.
        /// </summary>
        static DollhousePiece Configure(GameObject go, Transform slot, float snapRadius)
        {
            // Convex, because a Rigidbody that is not kinematic cannot carry a concave collider.
            // A wall slab's hull is close to the slab, and fragment-to-fragment collision is
            // switched off anyway, so the usual complaint about hulls being fat does not bite here.
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            collider.convex = true;

            go.AddComponent<NetworkObject>();

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 1f;

            // Hanging, not falling: these are 碎片 in mid-air, not objects on a table. The first
            // grab is also the last time gravity is asked about -- forceGravityOnDetach stays off
            // below, so a fragment put down stays where it was put, which is the whole point of
            // being able to let go of one and line another up.
            rb.useGravity = false;
            rb.isKinematic = false;
            rb.linearDamping = 2f;
            rb.angularDamping = 2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            go.AddComponent<ClientNetworkTransform>();

            var grab = go.AddComponent<XRGrabInteractable>();
            grab.useDynamicAttach = true;        // held where the hand closed, not snapped to the pivot
            grab.throwOnDetach = false;          // meant to be carried home, not thrown
            grab.forceGravityOnDetach = false;   // and to stay where it is let go of

            // spawnLocked freezes the body until the first grab, which is what stops an invisible
            // fragment being knocked across the room during the half hour before this beat. It also
            // hands ownership to whoever grabs it, which is what lets their ClientNetworkTransform
            // write the pose the other machine reads.
            var physics = go.AddComponent<NetworkPhysicsInteractable>();
            physics.spawnLocked = true;

            // AND NO OWNERSHIP EXCHANGE ON COLLISION. The default hands a piece to whoever it
            // bumps into, which in a beat where both players are carrying fragments through the
            // same volume of air means a piece changing hands because somebody's wall brushed past
            // it. 「先搶先贏」 is decided by the grab, not by a collision.
            var physicsSo = new SerializedObject(physics);
            physicsSo.FindProperty("m_AllowCollisionOwnershipExchange").boolValue = false;
            physicsSo.ApplyModifiedPropertiesWithoutUndo();

            var piece = go.AddComponent<DollhousePiece>();
            var so = new SerializedObject(piece);
            so.FindProperty("m_Slot").objectReferenceValue = slot;
            so.FindProperty("m_SnapRadius").floatValue = snapRadius;
            so.FindProperty("m_Interactable").objectReferenceValue = grab;
            so.FindProperty("m_Rigidbody").objectReferenceValue = rb;
            so.ApplyModifiedPropertiesWithoutUndo();

            return piece;
        }

        /// <summary>
        /// 「在 playspace 中央空中」, on the side of the ring this piece belongs on. The angle comes
        /// from the slot's own direction out of the base, so the left wall waits out to the left;
        /// a piece whose slot is dead centre falls back to its place in the order.
        /// </summary>
        /// <summary>How far out the ring sits: clear of the outermost slot, and still reachable.</summary>
        static float RingRadiusFor(Transform baseT, List<Transform> slots)
        {
            float furthest = 0f;
            foreach (Transform slot in slots)
            {
                Vector3 outward = slot.position - baseT.position;
                float flat = new Vector2(outward.x, outward.z).magnitude;
                if (flat > furthest) furthest = flat;
            }

            float radius = Mathf.Clamp(furthest + k_ScatterClearance, k_ScatterMin, k_ScatterMax);
            Debug.Log($"[S2-3] The outermost slot is {furthest:0.000} m out, so the fragments wait " +
                      $"on a ring {radius:0.000} m from the base.");
            return radius;
        }

        static Vector3 ScatterPose(Transform baseT, Transform slot, int index, int count,
                                   float radius)
        {
            Vector3 outward = slot.position - baseT.position;
            Vector2 flat = new(outward.x, outward.z);

            float angle = flat.sqrMagnitude > 1e-6f
                ? Mathf.Atan2(flat.y, flat.x)
                : index * Mathf.PI * 2f / Mathf.Max(count, 1);

            float height = count > 1
                ? Mathf.Lerp(-k_ScatterDrop, k_ScatterRise, index / (float)(count - 1))
                : 0f;

            return new Vector3(baseT.position.x + Mathf.Cos(angle) * radius,
                               baseT.position.y + height,
                               baseT.position.z + Mathf.Sin(angle) * radius);
        }

        /// <summary>
        /// How close is close enough, from the house's own geometry: a third of the gap to the
        /// nearest other slot. Tight enough that a fragment cannot look misplaced and still snap,
        /// loose enough to be reachable with a controller in a dark room.
        /// </summary>
        static float SnapRadiusFor(List<Transform> slots)
        {
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < slots.Count; i++)
            {
                for (int j = i + 1; j < slots.Count; j++)
                {
                    float d = Vector3.Distance(slots[i].position, slots[j].position);
                    if (d < nearest) nearest = d;
                }
            }

            if (float.IsInfinity(nearest)) return k_MaxSnap;

            float snap = Mathf.Clamp(nearest * k_SnapFraction, k_MinSnap, k_MaxSnap);
            Debug.Log($"[S2-3] Nearest two slots are {nearest:0.000} m apart, so a fragment snaps " +
                      $"at {snap:0.000} m.");
            return snap;
        }

        static MemoryDissolve BuildLowPolyDissolve(GameObject root,
                                                   List<DollhouseAssembly.Piece> pieces,
                                                   GameObject baseGo)
        {
            var go = new GameObject("LowPoly Dissolve");
            go.transform.SetParent(root.transform, false);

            var renderers = new List<Renderer>();
            if (baseGo != null) renderers.AddRange(baseGo.GetComponentsInChildren<Renderer>(true));

            foreach (DollhouseAssembly.Piece p in pieces)
            {
                if (p.fragment != null)
                    renderers.AddRange(p.fragment.GetComponentsInChildren<Renderer>(true));
                if (p.big != null)
                    renderers.AddRange(p.big.GetComponentsInChildren<Renderer>(true));
            }

            var dissolve = go.AddComponent<MemoryDissolve>();
            var so = new SerializedObject(dissolve);
            SerializedProperty list = so.FindProperty("m_Renderers");
            list.arraySize = renderers.Count;
            for (int i = 0; i < renderers.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[S2-3] The low-poly house dissolves as one group of {renderers.Count} " +
                      "renderers -- both sizes together, 「都會 dissolve out」.");
            return dissolve;
        }

        /// <summary>
        /// Inner_Wall's own renderers, moved onto the dissolving shader for good.
        ///
        /// NOT SWAPPED BACK AT THE END, which is the one judgement call in here. A material that
        /// goes in for the dissolve and comes out again pops on the frame of the swap, and for a
        /// dissolve-IN that frame is the last one -- the frame the whole beat has been building to.
        /// MemoryFragments is a complete lit shader (lightmaps, shadowmask, the main light, the
        /// additional lights, fog, and it casts shadows), and the wall's own materials carry a base
        /// map and nothing else, so there is nothing to lose by staying on it. A corrected house
        /// that brings normal or metallic maps with it would change that, which is why this warns.
        ///
        /// The MemoryDissolve itself goes on this root rather than on Inner_Wall, so the only thing
        /// this leaves on the section's prefab instance is the material override -- revertible with
        /// a right-click if any of this turns out wrong.
        /// </summary>
        static MemoryDissolve BuildInnerWallDissolve(GameObject root, Scene scene)
        {
            GameObject innerWall = Find(scene, k_InnerWall);
            if (innerWall == null)
            {
                Debug.LogWarning($"[S2-3] No '{k_InnerWall}' in MainScene, so nothing will arrive " +
                                 "when the low-poly house blows away.");
                return null;
            }

            var go = new GameObject("Inner Wall Dissolve");
            go.transform.SetParent(root.transform, false);

            var renderers = new List<Renderer>();
            var bySource = new Dictionary<Material, Material>();

            foreach (MeshRenderer r in innerWall.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] originals = r.sharedMaterials;
                var swapped = new Material[originals.Length];
                bool changed = false;

                for (int i = 0; i < originals.Length; i++)
                {
                    Material source = originals[i];
                    if (source == null) continue;

                    if (source.shader != null && source.shader.name == k_DissolveShader)
                    {
                        // Already done by an earlier run.
                        swapped[i] = source;
                        continue;
                    }

                    if (!bySource.TryGetValue(source, out Material dissolve))
                    {
                        dissolve = DissolveMaterialFor(source, source.name);
                        bySource[source] = dissolve;
                    }

                    swapped[i] = dissolve;
                    changed = changed || dissolve != source;
                }

                if (changed) r.sharedMaterials = swapped;
                renderers.Add(r);
            }

            var dissolve2 = go.AddComponent<MemoryDissolve>();
            var so = new SerializedObject(dissolve2);
            SerializedProperty list = so.FindProperty("m_Renderers");
            list.arraySize = renderers.Count;
            for (int i = 0; i < renderers.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[S2-3] '{k_InnerWall}': {renderers.Count} renderers on " +
                      $"{bySource.Count} dissolving material(s).");
            return dissolve2;
        }

        static void WireAssembly(GameObject root, Transform baseT,
                                 List<DollhouseAssembly.Piece> pieces, GameObject beat,
                                 MemoryDissolve lowPoly, MemoryDissolve innerWall)
        {
            var assembly = root.GetComponent<DollhouseAssembly>();
            if (assembly == null) assembly = root.AddComponent<DollhouseAssembly>();

            var so = new SerializedObject(assembly);
            so.FindProperty("m_Base").objectReferenceValue = baseT;
            so.FindProperty("m_BeatRoot").objectReferenceValue = beat;
            so.FindProperty("m_LowPolyDissolve").objectReferenceValue = lowPoly;
            so.FindProperty("m_InnerWallDissolve").objectReferenceValue = innerWall;

            // Written every run rather than left to the component's default, because the default
            // belongs to a component somebody may have added by hand and this number has to agree
            // with the beat's clip. See k_AutoCompleteAfter.
            so.FindProperty("m_AutoCompleteAfter").floatValue = k_AutoCompleteAfter;

            SerializedProperty list = so.FindProperty("m_Pieces");
            list.arraySize = pieces.Count;
            for (int i = 0; i < pieces.Count; i++)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("fragment").objectReferenceValue = pieces[i].fragment;
                entry.FindPropertyRelative("big").objectReferenceValue = pieces[i].big;
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[S2-3] Beat wired: {pieces.Count} entries, armed by '{beat.name}', " +
                      $"finishing itself {k_AutoCompleteAfter:0}s in.");
        }

        #endregion

        #region Assets and lookups

        /// <summary>
        /// A dissolving twin of one material: the same base map, colour and smoothness on the
        /// MemoryFragments shader, with the dissolve wound back to nothing so the editor shows the
        /// thing rather than half of it.
        ///
        /// Re-used rather than rebuilt, so the particle size and tint somebody dialled in on a
        /// previous pass survive a re-run -- only a material this call creates is written to.
        /// </summary>
        static Material DissolveMaterialFor(Renderer renderer, string name) =>
            renderer == null || renderer.sharedMaterial == null
                ? null
                : DissolveMaterialFor(renderer.sharedMaterial, name);

        static Material DissolveMaterialFor(Material source, string name)
        {
            Shader shader = Shader.Find(k_DissolveShader);
            if (shader == null)
            {
                Debug.LogError($"[S2-3] RESULT: FAIL - shader '{k_DissolveShader}' not found.");
                return null;
            }

            string path = $"{k_DissolveDir}/{name}_Dissolve.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            Directory.CreateDirectory(k_DissolveDir);
            mat = new Material(shader) { name = $"{name}_Dissolve" };

            if (source != null)
            {
                if (source.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
                if (source.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", source.GetColor("_BaseColor"));
                if (source.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", source.GetFloat("_Smoothness"));

                // Said rather than silently dropped: a later house with real surface maps would
                // lose them here, and the fix is to dissolve a copy instead of the wall itself.
                foreach (string lost in new[] { "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap" })
                {
                    if (source.HasProperty(lost) && source.GetTexture(lost) != null)
                        Debug.LogWarning($"[S2-3] '{source.name}' has a {lost} that " +
                                         $"'{mat.name}' cannot carry. The dissolve will look " +
                                         "flatter than the wall it replaces.");
                }
            }

            // The wall is THERE until something says otherwise. The shader's own default is a half
            // dissolve, which would leave the scene view showing a wall full of holes.
            mat.SetFloat("_Progress", 0f);

            AssetDatabase.CreateAsset(mat, path);
            EditorUtility.SetDirty(mat);
            Debug.Log($"[S2-3] Made {path}.");
            return mat;
        }

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
                Debug.LogError("[S2-3] Could not find NetworkObject.OnValidate; hashes may stay 0.");
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

        static void Verify(GameObject root, GameObject baseGo, List<DollhousePiece> fragments,
                           List<DollhouseAssembly.Piece> pieces, List<Transform> slots, float snap,
                           MemoryDissolve innerWall)
        {
            bool ok = true;

            ok &= Check(root.activeInHierarchy,
                        "the dollhouse root is active, so Netcode will spawn the fragments",
                        "the dollhouse root is NOT active -- Netcode collects in-scene " +
                        "NetworkObjects from what is active, so no fragment would ever spawn");

            foreach (DollhousePiece piece in fragments)
            {
                ok &= Check(piece.slot != null,
                            $"{piece.name} knows where it belongs",
                            $"{piece.name} has no slot and can never snap");

                ok &= Check(piece.GetComponent<ClientNetworkTransform>() != null,
                            $"{piece.name} replicates its pose",
                            $"{piece.name} has no ClientNetworkTransform, so the other machine " +
                            "never sees it move and it can never be judged close");

                ok &= Check(piece.GetComponent<XRGrabInteractable>() != null,
                            $"{piece.name} can be picked up",
                            $"{piece.name} has no XRGrabInteractable");

                uint hash = HashOf(piece.GetComponent<NetworkObject>());
                ok &= Check(hash != 0u,
                            $"{piece.name} has network hash {hash}",
                            $"{piece.name} has hash 0 and can never be matched between machines");

                // A FRAGMENT THAT STARTS INSIDE ITS OWN SNAP RADIUS ASSEMBLES ITSELF the moment
                // somebody brushes it. The scatter puts them a ring's width away, so this is a
                // check on the ring, not on the pieces.
                ok &= Check(piece.distanceToSlot > snap * 2f,
                            $"{piece.name} starts {piece.distanceToSlot:0.00} m from its slot",
                            $"{piece.name} starts {piece.distanceToSlot:0.00} m from its slot, " +
                            $"which is inside twice the {snap:0.00} m snap -- it would go home on " +
                            "the first touch");
            }

            // THE SLOTS HAVE TO REBUILD THE AUTHORED HOUSE. Each slot was taken from the authored
            // small piece, so a fragment at its slot occupies exactly the space the authored piece
            // did; this checks the copy did not drift -- a different lossy scale, a parent with a
            // rotation, anything that would leave a house with a step in it.
            float worst = 0f;
            for (int i = 0; i < fragments.Count; i++)
            {
                float d = Vector3.Distance(slots[i].position, fragments[i].slot.position);
                if (d > worst) worst = d;
            }
            ok &= Check(worst < 0.0005f,
                        $"every slot sits where its authored piece did ({worst * 1000f:0.0} mm out)",
                        $"a slot is {worst * 1000f:0.0} mm from the piece it was taken from");

            int walls = 0;
            foreach (DollhouseAssembly.Piece p in pieces)
            {
                if (p.big != null) walls++;
            }
            ok &= Check(walls == pieces.Count,
                        $"all {walls} big walls are built and will come down",
                        $"only {walls} of {pieces.Count} entries have a big wall, so some " +
                        "fragments will land and nothing will answer them");

            ok &= Check(baseGo.GetComponentInChildren<Collider>(true) == null,
                        "the base has no collider, so nothing can be held out of its slot",
                        "the base has a collider and may hold a fragment out of its own slot");

            ok &= Check(innerWall != null,
                        "Inner_Wall is wired and will arrive",
                        "Inner_Wall was not found, so the beat ends with an empty room");

            Debug.Log(ok
                ? $"[S2-3] RESULT: PASS - {fragments.Count} fragments, {walls} walls, snapping at " +
                  $"{snap:0.000} m, armed by Beat_S2-3."
                : "[S2-3] RESULT: FAIL - see the lines above.");
        }

        static bool Check(bool ok, string good, string bad)
        {
            if (ok) Debug.Log($"[S2-3] OK   {good}");
            else Debug.LogError($"[S2-3] FAIL {bad}");
            return ok;
        }

        #endregion
    }
}
