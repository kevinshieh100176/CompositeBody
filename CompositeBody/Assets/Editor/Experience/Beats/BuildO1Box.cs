using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using CompositeBody.Experience;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// O-1 becomes two halves: the object is on the FLOOR, and there is a cardboard box to put
    /// it in. The player bends down, picks their thing up, and puts it away; the beat's gate is
    /// the box, not the grab.
    ///
    /// THREE THINGS HAVE TO BE TRUE FOR THAT, and none of them were:
    ///
    /// 1. THE ROOM HAD NO FLOOR. Not a visible one -- Content/Grey Plane is a renderer with no
    ///    collider -- which is why the props were built with gravity switched off and hung in
    ///    mid-air at chest height. An object you are asked to pick UP has to be able to rest on
    ///    something, and one you drop on the way to the box has to land rather than fall out of
    ///    the world, so an invisible floor goes in at y=0 and gravity goes back on.
    /// 2. THE PROPS WERE STAGED IN THE AIR. They are lowered ONCE, here, each by the exact
    ///    distance that puts the bottom of its own bounds on the floor -- their x and z are left
    ///    alone, because where in the room a thing stands is a hand decision. A prop already on
    ///    the floor is not touched, so this can be re-run.
    /// 3. THE BOX IS A CHILD OF THE STATION, which is what makes it fade in with the object it
    ///    belongs to: <see cref="ScanHandover"/> collects the renderers under the prop's station
    ///    at runtime, and <see cref="RoleVisibility"/> on that same station hides the whole lot
    ///    -- renderers and colliders -- from the player it does not belong to. One box each,
    ///    neither player able to see or reach the other's, exactly as the coat and the mug
    ///    already are. Nothing here is networked, because nothing here is ever seen twice.
    ///
    /// THE CARTON IS THE MODEL at one hand-chosen scale, the same for both players: they are
    /// the same carton twice, and how big a cardboard box is was decided by eye against the
    /// room rather than calculated from what goes in it. What IS checked is that the thing
    /// still fits, which is a warning in the log and not a number that quietly moves.
    ///
    /// WHERE IT STANDS IS A RULE, NOT STAGING -- the edge of the play space, on the side its
    /// object is on, far enough back to leave that object in sight. So it is re-placed on every
    /// run, and a box moved by hand will not stay moved. Its collider is a mesh collider and
    /// NOT convex: a convex hull of a carton is a solid block, and the one thing
    /// this box has to allow is something going inside it. A concave mesh collider is only
    /// allowed on something static, which this is -- the box has no Rigidbody and never moves.
    ///
    /// ITS MATERIAL IS A TRANSPARENT COPY of the model's own, textures and all, because the box
    /// fades in with the object: ScanHandover drives the alpha of every renderer under the
    /// station, and an opaque carton would simply appear on the frame the fade passed zero.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildO1Box.Run
    /// </summary>
    public static class BuildO1Box
    {
        const string k_PrefabPath = "Assets/_Scenes/Onboarding.prefab";

        const string k_Model = "Assets/_models/3d-cardboard-lowpoly/source/Cardboard.fbx";
        const string k_ModelMaterial = "Assets/_models/3d-cardboard-lowpoly/source/Cardbox_MAT.mat";
        const string k_Material = "Assets/Materials/O1Cardboard.mat";

        /// <summary>What the carton is called under the box, and how a re-run recognises it.</summary>
        const string k_CartonName = "Carton";

        /// <summary>The floor the props rest on, and the one they are lowered to.</summary>
        const float k_FloorY = 0f;

        /// <summary>
        /// THE SIZE, as a scale on the model, exactly as the carton staged in MainScene was
        /// sized by hand. Not worked out from the object any more: how big a cardboard box is
        /// is a look, it was decided by eye against the room, and one number here is what keeps
        /// both players' boxes identical.
        /// </summary>
        const float k_CartonScale = 1.5f;

        /// <summary>
        /// The debug play-space outline, 4 x 4 m centred on the origin -- the floor the players
        /// actually have. The boxes stand against its edge.
        /// </summary>
        const float k_PlaySpace = 4f;

        /// <summary>How far inside the outline the carton's own edge stops.</summary>
        const float k_EdgeMargin = 0.05f;

        /// <summary>
        /// How far down the room both boxes stand. One number rather than a distance from each
        /// object: the two boxes are a pair and they line up with each other, which an offset
        /// measured from two differently placed props would not.
        /// </summary>
        const float k_BoxZ = 0.75f;

        /// <summary>
        /// How much of the carton's own footprint counts as its inside. The walls take the rest,
        /// and a trigger that reached them would catch a coat leaned against the outside.
        /// </summary>
        const float k_Inside = 0.78f;

        [MenuItem("Tools/O-1/Build the Drop Boxes")]
        public static void Run()
        {
            Debug.Log("[O1Box] Starting...");

            Mesh carton = LoadMesh(k_Model);
            if (carton == null)
            {
                Debug.LogError($"[O1Box] RESULT: FAIL - no mesh inside {k_Model}.");
                return;
            }

            Material cardboard = BuildCardboard();
            if (cardboard == null)
            {
                Debug.LogError($"[O1Box] RESULT: FAIL - could not build a material from " +
                               $"{k_ModelMaterial}.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(k_PrefabPath);
            if (root == null)
            {
                Debug.LogError($"[O1Box] RESULT: FAIL - could not open {k_PrefabPath}.");
                return;
            }

            try
            {
                Transform group = FindDeep(root.transform, "O_1 Grab");
                if (group == null)
                {
                    Debug.LogError("[O1Box] RESULT: FAIL - no Beats/O_1 Grab in the prefab. Run " +
                                   "Tools/Onboarding/Build Onboarding first.");
                    return;
                }

                EnsureFloor(group);

                if (!Prepare(root.transform, "Coat (Player A)", PlayerRole.Player1, out Staged a))
                    return;
                if (!Prepare(root.transform, "Mug (Player B)", PlayerRole.Player2, out Staged b))
                    return;

                // ONE SCALE, BOTH BOXES. The model carries its own units through whatever
                // transforms the FBX has inside it, so the scale that reproduces the carton
                // staged in the scene is that chain times k_CartonScale -- read from the model
                // rather than written down, so this still matches if the asset is reimported.
                Vector3 scale = ModelScale(k_Model) * k_CartonScale;

                Fit(a, carton, cardboard, scale);
                Fit(b, carton, cardboard, scale);

                PrefabUtility.SaveAsPrefabAsset(root, k_PrefabPath);
                AssetDatabase.SaveAssets();

                Debug.Log("[O1Box] Each player has their own carton, under their own station, so " +
                          "it fades in with their object and is hidden from the other player.\n" +
                          "  The gate 'beat.O1_Control' is now reported by the box, not the grab.\n" +
                          "[O1Box] RESULT: PASS");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>One player's station, measured and put on the floor, ready to be built on.</summary>
        struct Staged
        {
            public Transform station;
            public XRGrabInteractable grab;
            public PlayerRole owner;
            public string name;

            /// <summary>Where the object ended up, once it was on the floor.</summary>
            public Bounds bounds;

            /// <summary>Its longest side: what a carton has to be able to take.</summary>
            public float longest;
        }

        /// <summary>
        /// Find a station, put its object on the floor and give it back its gravity, then
        /// measure it. Nothing is built here -- the carton's size depends on BOTH stations, so
        /// both are measured before either is filled.
        /// </summary>
        static bool Prepare(Transform root, string stationName, PlayerRole owner, out Staged staged)
        {
            staged = default;

            Transform station = FindDeep(root, stationName);
            if (station == null)
            {
                Debug.LogError($"[O1Box] RESULT: FAIL - no '{stationName}' in the prefab. Run " +
                               "Tools/O-1/Build Grab Interaction first.");
                return false;
            }

            var grab = station.GetComponentInChildren<XRGrabInteractable>(true);
            if (grab == null)
            {
                Debug.LogError($"[O1Box] RESULT: FAIL - nothing grabbable under '{stationName}'.");
                return false;
            }

            Bounds bounds = Lower(grab.transform, stationName);
            Fall(grab.gameObject, stationName);

            staged = new Staged
            {
                station = station,
                grab = grab,
                owner = owner,
                name = stationName,
                bounds = bounds,
                longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)),
            };
            return true;
        }

        /// <summary>
        /// One station's box: a carton of the shared size, stood against the play space's edge
        /// beside its object, watching what goes into it.
        ///
        /// NOTHING HERE IS STAGED BY HAND ANY MORE, which is a change from how this started.
        /// The size is one number for both players and the place is a rule -- the edge of the
        /// floor the players actually have -- so both are worked out every run, and a box moved
        /// by hand goes back on the next one. That is the trade for the two boxes being
        /// identical and for neither of them standing in front of the thing it is for.
        /// </summary>
        static void Fit(Staged staged, Mesh carton, Material cardboard, Vector3 scale)
        {
            Transform box = staged.station.Find("Box");
            if (box == null)
            {
                var go = new GameObject("Box");
                go.transform.SetParent(staged.station, false);
                box = go.transform;
            }

            for (int i = box.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(box.GetChild(i).gameObject);

            Vector3 size = Fill(box, carton, cardboard, scale, staged.name);
            CheckItFits(staged, size);
            Place(box, staged, size);
            Wire(box.gameObject, staged.owner, staged.grab);
        }

        /// <summary>
        /// Against the edge of the play space, on the side its object is on, at the shared depth
        /// both boxes stand at. The outline is the floor the players really have; a box outside
        /// it is a box someone has to leave the room to reach.
        /// </summary>
        static void Place(Transform box, Staged staged, Vector3 size)
        {
            float side = staged.bounds.center.x >= 0f ? 1f : -1f;
            float x = side * Mathf.Max(k_PlaySpace * 0.5f - size.x * 0.5f - k_EdgeMargin, 0f);

            box.position = new Vector3(x, k_FloorY, k_BoxZ);

            Debug.Log($"[O1Box] {staged.name}: box at ({x:F2}, {k_FloorY:F2}, {k_BoxZ:F2}), " +
                      $"against the {(side > 0 ? "+X" : "-X")} edge of the {k_PlaySpace:F1} m " +
                      $"play space, {staged.bounds.center.z - k_BoxZ:F2} m from its object.");
        }

        /// <summary>
        /// The carton itself and the volume inside it. The trigger is worked out from the
        /// carton's own scaled bounds, so it follows the box rather than being a number written
        /// down beside it. Returns the carton's size in metres, which is what the placement and
        /// the fit warning are both read off.
        /// </summary>
        static Vector3 Fill(Transform box, Mesh carton, Material cardboard, Vector3 scale,
                            string stationName)
        {
            Bounds mesh = carton.bounds;

            var piece = new GameObject(k_CartonName);
            piece.transform.SetParent(box, false);
            piece.transform.localScale = scale;

            // Centred on the box and standing on the floor, whatever the model's own pivot is.
            piece.transform.localPosition = new Vector3(-mesh.center.x * scale.x,
                                                        -mesh.min.y * scale.y,
                                                        -mesh.center.z * scale.z);

            piece.AddComponent<MeshFilter>().sharedMesh = carton;
            piece.AddComponent<MeshRenderer>().sharedMaterial = cardboard;

            // NOT CONVEX. A convex hull of a carton is a solid block with a lid, and the whole
            // point of this one is that something goes inside it. Legal here because the box is
            // static: it has no Rigidbody and never moves.
            var collider = piece.AddComponent<MeshCollider>();
            collider.sharedMesh = carton;
            collider.convex = false;

            Vector3 size = Vector3.Scale(mesh.size, scale);

            // THE INSIDE, as a trigger. Inset from the walls so a coat leaned against the
            // outside does not count, and half again as tall as the carton so one that ends up
            // lying across the rim does: what is being asked is "did they put it in", not "does
            // it fit". It starts at the floor, which is where a thing that is in a box is.
            var mouth = new GameObject("Mouth");
            mouth.transform.SetParent(box, false);
            var volume = mouth.AddComponent<BoxCollider>();
            volume.isTrigger = true;
            volume.center = new Vector3(0f, size.y * 0.75f, 0f);
            volume.size = new Vector3(size.x * k_Inside, size.y * 1.5f, size.z * k_Inside);

            Debug.Log($"[O1Box] {stationName}: carton at scale {scale.x:F3} is " +
                      $"{size.x:F2}x{size.y:F2}x{size.z:F2} m; " +
                      $"mouth {volume.size.x:F2}x{volume.size.z:F2} m.");

            return size;
        }

        /// <summary>
        /// Whether the object can actually be got into the box. The object is rigid -- a coat on
        /// a hanger does not fold -- so what it needs is the diagonal of the carton's floor;
        /// anything longer comes to rest balanced on the rim instead of going in, and the player
        /// is left pushing at it while the show holds. A warning rather than a failure, because
        /// the sizes are a look and the person changing them should be the one to judge it.
        /// </summary>
        static void CheckItFits(Staged staged, Vector3 size)
        {
            float diagonal = Mathf.Sqrt(size.x * size.x + size.z * size.z) * k_Inside;
            if (staged.longest <= diagonal) return;

            Debug.LogWarning($"[O1Box] {staged.name}: the object is {staged.longest:F2} m long " +
                             $"and the inside of the carton measures {diagonal:F2} m corner to " +
                             "corner, so it may not go in. Raise k_CartonScale, or expect it to " +
                             "sit across the rim -- the mouth is tall enough to count that, but " +
                             "it will not look like a thing that was put away.", staged.station);
        }

        /// <summary>
        /// Drop the object straight down until the bottom of its own bounds is on the floor. Its
        /// x and z are not touched: those were staged by hand, and the beat's blocking depends on
        /// which 真人 each object was left behind by.
        ///
        /// Returns the bounds it ends up with, which is what the box is sized and placed from.
        /// </summary>
        static Bounds Lower(Transform prop, string stationName)
        {
            Bounds bounds = WorldBounds(prop);
            float drop = bounds.min.y - k_FloorY;

            if (Mathf.Abs(drop) < 0.01f)
            {
                Debug.Log($"[O1Box] {stationName}: already on the floor.");
                return bounds;
            }

            prop.position -= new Vector3(0f, drop, 0f);
            bounds.center -= new Vector3(0f, drop, 0f);

            Debug.Log($"[O1Box] {stationName}: lowered {drop:F2} m onto the floor; it is " +
                      $"{bounds.size.x:F2}x{bounds.size.y:F2}x{bounds.size.z:F2} m.");
            return bounds;
        }

        /// <summary>
        /// Gravity back on, now that there is something to land on. These were built kinematic
        /// in the air for want of a floor; an object meant to be picked up off the ground and
        /// dropped into a box has to be a thing that falls, or the player's release leaves a mug
        /// hanging over the carton.
        /// </summary>
        static void Fall(GameObject prop, string stationName)
        {
            var rb = prop.GetComponent<Rigidbody>();
            if (rb == null)
            {
                Debug.LogWarning($"[O1Box] {stationName}: no Rigidbody, so it cannot fall or be " +
                                 "dropped. Left alone.");
                return;
            }

            if (rb.useGravity) return;

            rb.useGravity = true;
            Debug.Log($"[O1Box] {stationName}: gravity on.");
        }

        /// <summary>
        /// Point the component at the box it is on and the object it is waiting for. Re-wired
        /// every run, because these are references rather than staging: a box whose object was
        /// rebuilt should find the new one.
        /// </summary>
        static void Wire(GameObject box, PlayerRole owner, XRGrabInteractable grab)
        {
            var drop = box.GetComponent<O1DropBox>();
            if (drop == null) drop = box.AddComponent<O1DropBox>();

            Collider mouth = null;
            foreach (Collider collider in box.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.isTrigger) continue;
                mouth = collider;
                break;
            }

            var so = new SerializedObject(drop);
            so.FindProperty("m_Owner").intValue = (int)owner;
            so.FindProperty("m_Mouth").objectReferenceValue = mouth;
            so.FindProperty("m_Object").objectReferenceValue = grab;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[O1Box] '{box.name}' under '{box.transform.parent.name}': {owner} fills " +
                      $"it with '{grab.name}', and that reports 'beat.O1_Control'.");
        }

        /// <summary>
        /// The invisible floor. Under the beat's own group rather than under Content, which the
        /// O-0 timeline animates BY PATH -- adding a name there is harmless today and a trap the
        /// day someone renames something to match.
        ///
        /// Wide enough to cover the whole room: an object dropped anywhere a player can walk has
        /// to land, and one that falls past y=0 is gone for the rest of the show.
        /// </summary>
        static void EnsureFloor(Transform group)
        {
            if (group.Find("Floor Collider") != null) return;

            var floor = new GameObject("Floor Collider");
            floor.transform.SetParent(group, false);
            floor.transform.position = new Vector3(0f, k_FloorY, 0f);

            var collider = floor.AddComponent<BoxCollider>();
            collider.size = new Vector3(16f, 0.2f, 16f);
            collider.center = new Vector3(0f, -0.1f, 0f);   // its top face is the floor

            Debug.Log("[O1Box] added an invisible floor at y=0; the room had renderers for its " +
                      "ground but nothing to stand an object on.");
        }

        /// <summary>
        /// The model's own material, copied and made transparent. Copied rather than written out
        /// property by property, so whatever the carton came textured with -- base, normal,
        /// metallic -- is what the room shows; only the surface mode is this project's business.
        /// </summary>
        static Material BuildCardboard()
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(k_ModelMaterial);
            if (source == null)
            {
                Debug.LogError($"[O1Box] no material at {k_ModelMaterial}.");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(k_Material);
            if (material == null)
            {
                material = new Material(source.shader) { name = "O1Cardboard" };
                AssetDatabase.CreateAsset(material, k_Material);
            }

            material.shader = source.shader;
            material.CopyPropertiesFromMaterial(source);
            material.shaderKeywords = source.shaderKeywords;

            // OPAQUE, UNLIKE THE COAT AND THE MUG. Those are transparent so ScanHandover can
            // fade them up in the 真人's place, and the carton was built the same way for
            // consistency. In this room that was wrong twice over: with ZWrite off its own
            // walls and flaps sorted against each other by accident, and the 光圈 and the beams
            // -- transparent themselves -- drew straight THROUGH it, so the box appeared as a
            // ghost with the purple dome showing through its near wall and flap.
            //
            // AND IT CANNOT BE FIXED BY TURNING ZWRITE BACK ON. URP re-derives _ZWrite,
            // _SrcBlend, _DstBlend and the render queue from _Surface whenever the material is
            // validated, so a transparent material that is told to write depth has it taken away
            // again on the next import. Opaque is the supported way to say "solid", and a
            // cardboard box is a solid thing.
            //
            // THE COST USED TO BE THE FADE -- alpha did nothing to it, so forceRenderingOff was
            // what governed and the box APPEARED rather than arriving. BuildO1Timeline solves
            // that without giving the opacity up: it writes noise into the base map's alpha and
            // clips against it, so the carton is solid while it is there and leaves in pieces.
            //
            // WHICH THIS WIPES OUT, because CopyPropertiesFromMaterial puts the model's own base
            // map and alpha-clip settings back. Run Tools/O-1/Build O-1 Timeline after this one;
            // the log below says so too.
            material.SetFloat("_Surface", 0f);                  // 0 opaque, 1 transparent
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            material.SetFloat("_ZWrite", 1f);
            material.SetShaderPassEnabled("ShadowCaster", true);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_SURFACE_TYPE_OPAQUE");
            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;

            EditorUtility.SetDirty(material);
            Debug.LogWarning($"[O1Box] {k_Material} rebuilt from the model's own {source.name} " +
                             "-- which has taken the dissolve off it. Run Tools/O-1/Build O-1 " +
                             "Timeline to put the clipped base map back, or the cartons will " +
                             "appear and vanish instead of dissolving.");
            return material;
        }

        /// <summary>
        /// The scale the model's own hierarchy puts on its mesh, which is what a scale typed
        /// into the inspector on the imported prefab is multiplied by. Read from the asset so
        /// that "the same size as the one in the scene" survives a reimport that changes the
        /// FBX's unit conversion.
        /// </summary>
        static Vector3 ModelScale(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var filter = asset != null ? asset.GetComponentInChildren<MeshFilter>(true) : null;
            if (filter == null)
            {
                Debug.LogWarning($"[O1Box] no MeshFilter inside {path}; assuming the model is " +
                                 "already in metres.");
                return Vector3.one;
            }

            return filter.transform.lossyScale;
        }

        static Mesh LoadMesh(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Mesh mesh) return mesh;
            }

            return null;
        }

        /// <summary>Every renderer under an object, in one box, in world space.</summary>
        static Bounds WorldBounds(Transform prop)
        {
            var renderers = prop.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(prop.position, Vector3.zero);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        static Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindDeep(parent.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
