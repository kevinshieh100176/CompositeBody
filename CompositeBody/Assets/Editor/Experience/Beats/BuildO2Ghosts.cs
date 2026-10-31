using UnityEditor;
using UnityEngine;
using CompositeBody.PointClouds;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// O-2 鬼魂交流: four 真人 scans, one at a time, standing in the middle of the play space while
    /// 「O-AB3_你有沒有發現」 plays over them. 「這段會有4個scan(fade in/fade out)在play space正中間」.
    ///
    /// ONE PLACE, FOUR PEOPLE, AND THAT IS THE POINT. O-0 put a 真人 in front of each player and
    /// kept them apart; this beat puts them in the same spot one after another, so what the room
    /// shows is a succession rather than a pair. They never overlap -- each has gone before the
    /// next begins to form -- because two point clouds in one position read as one broken cloud,
    /// not as two ghosts.
    ///
    /// THE CENTRE IS THE ORIGIN, and that is not the same as where the players are. The play
    /// space is the 1.9 m Player Plate at Content's origin and the two spawns sit at z = -1.2,
    /// so a figure at (0, 0, 0) stands about a metre and a bit in front of both of them -- in
    /// the space between the players and the 真人 plates of O-0, which is the one piece of floor
    /// this show has kept empty.
    ///
    /// THE CAPTURES ARE THIS BEAT'S OWN, from the 2026-10-30 session, and they are named after
    /// the beat rather than after a performer: nothing in the files says who is in them, and
    /// Figure_A/Figure_B would be a claim this code cannot make. They stood in for A3/B3/A4/B4
    /// -- captures borrowed from the ten O-0 and S1 use -- which is why the earlier ones are
    /// still in the project and no longer referenced here.
    ///
    /// THE ORDER IS CHOSEN, NOT CHRONOLOGICAL, and it was confirmed as such. The captures run
    /// 11:19:01, 11:22:27, 11:25:56, 11:30:28; the succession runs 11:25:56, 11:30:28,
    /// 11:22:27, 11:19:01 -- leaning together, then face to face with their hands joined, then
    /// the embrace, then holding hands a little apart. Shooting order would have given the
    /// opposite shape, and this is the one that was asked for.
    ///
    /// ALL FOUR ARE THE SAME PAIR, which is why they are half again as wide as the 真人 of O-0
    /// and S1: those are one person each, these are two. It also means each body carries about
    /// a third of the points one of O-0's does, so at the shared material's 4.5 mm they read
    /// thinner. If that is too thin in a headset the fix is a material of their own, not a
    /// re-import -- O0FigureLit belongs to O-0's 真人 and changing it changes them.
    ///
    /// NOT NETWORKED, like the rest of the onboarding. Both players see the same figure in the
    /// same place, which is what the shared timeline already guarantees -- there is no state
    /// here for two machines to disagree about, and no RoleVisibility either, because for once
    /// the thing in the room belongs to both of them.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildO2Ghosts.Run
    /// </summary>
    public static class BuildO2Ghosts
    {
        const string k_PrefabPath = "Assets/_Scenes/O_2.prefab";

        /// <summary>
        /// The 真人 material O-0 built, borrowed rather than copied. These are the same people
        /// the audience met at the start of the show and they should be lit the same way; a
        /// second material would be a second set of point-size and exposure values to keep in
        /// step with the first by hand. Read here, never written.
        /// </summary>
        const string k_MaterialPath = "Assets/Materials/O0FigureLit.mat";

        /// <summary>One of O-2's four scans: what it is called, and whose capture it shows.</summary>
        public readonly struct Ghost
        {
            public readonly string name, mesh, capture;

            public Ghost(string name, string capture)
            {
                this.name = name;
                this.capture = capture;
                mesh = $"Assets/_models/Pointcloud/{capture}.ply";
            }

            /// <summary>Where the animation track writes, relative to the group's Animator.</summary>
            public string FigurePath => $"{name}/Figure";
        }

        /// <summary>
        /// The four, IN THE ORDER THEY APPEAR. <see cref="BuildO2Timeline"/> gives each one a
        /// slot of the beat in this order, so moving a row moves when that person is seen.
        /// </summary>
        public static readonly Ghost[] Ghosts =
        {
            new Ghost("Ghost 1", "Figure_O2_1"),
            new Ghost("Ghost 2", "Figure_O2_2"),
            new Ghost("Ghost 3", "Figure_O2_3"),
            new Ghost("Ghost 4", "Figure_O2_4"),
        };

        /// <summary>
        /// The middle of the play space, in Content's own space -- which is world space, because
        /// the onboarding root and its Content both sit at the origin unrotated.
        /// </summary>
        public static readonly Vector3 Centre = Vector3.zero;

        /// <summary>
        /// Which way they face. 0 is the capture's own facing, the same one O-0's 真人 have
        /// before their ±22° turn towards each other -- so these four stand exactly as the pair
        /// the audience has already been looking at for two minutes. One number if that turns
        /// out to be the wrong way round in a headset.
        /// </summary>
        const float k_Yaw = 0f;

        [MenuItem("Tools/O-2/Build the Ghost Scans")]
        public static void Run()
        {
            Debug.Log("[O2Ghosts] Starting...");

            var root = new GameObject("O_2");
            var content = new GameObject("Content");
            content.transform.SetParent(root.transform, false);

            if (!BuildInto(content.transform))
            {
                Object.DestroyImmediate(root);
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(root, k_PrefabPath, out bool saved);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();

            if (!saved)
            {
                Debug.LogError($"[O2Ghosts] RESULT: FAIL - could not write {k_PrefabPath}.");
                return;
            }

            Debug.Log($"[O2Ghosts] {k_PrefabPath}\n[O2Ghosts] RESULT: PASS");
        }

        /// <summary>
        /// Put the four scans under an existing parent, for the merged Onboarding prefab. Like
        /// O-1's grab stations, the beat is some content and a rule about when it is seen; it
        /// has no room of its own.
        ///
        /// Returns false if a capture or the material is missing, so a caller building something
        /// larger can stop rather than save a prefab with a hole in it.
        /// </summary>
        public static bool BuildInto(Transform parent)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(k_MaterialPath);
            if (material == null)
            {
                Debug.LogError($"[O2Ghosts] RESULT: FAIL - no material at {k_MaterialPath}. Run " +
                               "Tools/O-0/Build the Arrival first; these borrow O-0's 真人 " +
                               "material rather than keeping one of their own.");
                return false;
            }

            foreach (Ghost ghost in Ghosts)
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ghost.mesh);
                if (mesh == null)
                {
                    Debug.LogError($"[O2Ghosts] RESULT: FAIL - no point cloud at {ghost.mesh}.");
                    return false;
                }

                if (!BuildGhost(parent, ghost, mesh, material)) return false;
            }

            return true;
        }

        /// <summary>
        /// Distance from a point to the furthest corner of a box -- which is to say the radius
        /// at which a sweep starting there has reached everything.
        ///
        /// ALL EIGHT CORNERS, NOT THE ONE IN THE POSITIVE OCTANT. The shortcut of taking
        /// |centre| + extents per axis is right only when the origin is the box's own, and
        /// m_DissolveFrom sits at chest height: the furthest corner from there is UNDER it, at
        /// the feet, and the shortcut quietly returns about half the real distance. Half a
        /// radius is not a visibly wrong number -- it saturates the order of every point beyond
        /// it, so the legs and the outer arms all arrive together in the last fifth of the
        /// curve instead of sweeping, and the sweep is the whole effect.
        /// </summary>
        static float FarthestCorner(Bounds bounds, Vector3 from)
        {
            float furthest = 0f;

            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (i & 4) == 0 ? bounds.min.z : bounds.max.z);

                furthest = Mathf.Max(furthest, Vector3.Distance(from, corner));
            }

            return furthest;
        }

        static bool BuildGhost(Transform parent, Ghost spec, Mesh mesh, Material material)
        {
            Transform station = parent.Find(spec.name);
            if (station == null)
            {
                var go = new GameObject(spec.name);
                go.transform.SetParent(parent, false);
                station = go.transform;
            }

            // REBUILT IN PLACE, not from scratch: the group's position is the one thing in this
            // beat somebody might reasonably move by hand in the scene view, and a builder that
            // destroyed and recreated it would quietly undo that every time it ran. A station
            // that has never existed gets the centre; one that already exists keeps where it is.
            if (station.childCount == 0)
            {
                station.localPosition = Centre;
                station.localRotation = Quaternion.Euler(0f, k_Yaw, 0f);
            }

            Transform figureT = station.Find("Figure");
            if (figureT == null)
            {
                var go = new GameObject("Figure");
                go.transform.SetParent(station, false);
                figureT = go.transform;
            }

            GameObject figure = figureT.gameObject;

            var filter = figure.GetComponent<MeshFilter>();
            if (filter == null) filter = figure.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = figure.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = figure.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            // The cloud's own bounds, grown: the shader pushes points around as they form and
            // dissolve, and a renderer culled on the mesh's resting bounds pops out of existence
            // at the edge of the view exactly when it is most visibly moving.
            Bounds bounds = mesh.bounds;
            bounds.Expand(2.5f);
            renderer.localBounds = bounds;

            // Nothing to receive from, and casting is what turns a point cloud's shadow into a
            // field of holes. Off on both counts, as O-0's 真人 are.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var cloud = figure.GetComponent<PointCloudFigure>();
            if (cloud == null) cloud = figure.AddComponent<PointCloudFigure>();

            // FORMED AND ENTIRELY DEPARTED, which is this beat's way of saying "not here yet".
            // 「我這四個想要都是dissolve in」 -- they arrive by running the dissolve BACKWARDS, so
            // the float the timeline drives is m_Dissolve going 1 -> 0 and m_Reveal is simply
            // pinned at 1 for the whole beat.
            //
            // WHY NOT REVEAL, WHICH IS THE ONE NAMED FOR THIS. Reveal is a hard spatial
            // wavefront: saturate((_Reveal - order) * 8), no per-point term at all, so it draws
            // the body on from an origin like a wipe. Dissolve mixes that order with a per-point
            // hash (m_DissolveScatter), drifts each point along m_DissolveDir as it goes, and
            // shrinks it -- so reversed it is a cloud of scattered points falling in and
            // settling into a person. That is the image, and reveal cannot make it.
            //
            // AND IT NEEDS NO RADIUS. PointDeparture multiplies the amount by 1.25 before
            // subtracting the threshold, so at m_Dissolve = 1 every point is gone whatever its
            // order -- where reveal at 1 leaves anything past order 0.875 still missing. The
            // reveal radius goes back to 0 for exactly that reason: with reveal pinned at 1 and
            // a radius set, the outermost points would sit permanently half-drawn.
            var so = new SerializedObject(cloud);
            so.FindProperty("m_Reveal").floatValue = 1f;
            so.FindProperty("m_Dissolve").floatValue = 1f;

            // AND A REVEAL RADIUS, OR THERE IS NO FADE AT ALL. 「o-2 每個 scan 都要fade in」 --
            // and with the radius left at its default of 0 they did not: PointSpatialOrder
            // returns 0 for every point when the radius is zero, so the shader's
            // saturate((_Reveal - order) * 8) is the same number for the whole cloud and
            // reaches 1 at _Reveal = 0.125. Over a 2.5-second curve that is a figure appearing
            // whole after three tenths of a second -- a pop with a very short ramp on it.
            //
            so.FindProperty("m_RevealRadius").floatValue = 0f;

            // WHERE THEY COME APART, AND THEREFORE WHERE THEY COME TOGETHER. The defaults are a
            // single figure's: an origin at chest height and a 1.8 m radius, which on one body
            // reaches everything. These are TWO people side by side, so the far shoulder sits
            // about 1.4 m from that origin and the sweep still covers them -- but only just,
            // and a wider pose would start arriving in two halves. Measured per capture for the
            // same reason the reveal radius was: a number that happens to fit today is a number
            // that breaks on the next capture without saying so.
            Vector3 from = so.FindProperty("m_DissolveFrom").vector3Value;
            float radius = FarthestCorner(mesh.bounds, from);

            so.FindProperty("m_DissolveRadius").floatValue = radius;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[O2Ghosts] {spec.name}: {spec.capture}, {mesh.vertexCount:N0} points, " +
                      $"{mesh.bounds.size.y:F2} m tall, standing at " +
                      $"{station.localPosition.ToString("F2")} fully dissolved, coming " +
                      $"back together towards {from.ToString("F2")} over {radius:F2} m.");
            return true;
        }
    }
}
