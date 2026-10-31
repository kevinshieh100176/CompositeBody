using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// O-1: each player has one object, and only they can see it. 「首先出現：燈（兩個顏色或是兩個樣式）」
    /// -- so both players get the SAME object and it is a lamp, told apart by its colour: each
    /// player's copy carries that player's own 代表色, A 湖水綠 and B 藍色, from
    /// <see cref="PlayerRoleColors"/>.
    ///
    /// IT USED TO BE A COAT AND A MUG, two different props from two different asset packs. The
    /// running order asks for one object in two colours instead, which is a smaller thing to
    /// read across a room and a much smaller thing to explain to a player who has sixty seconds
    /// to work out what is being asked of them. Both old models are still in the project; the
    /// stations are renamed here and a legacy pair is migrated in place, so a prefab built
    /// before this change does not end up with four stations in it.
    ///
    /// NOT NETWORKED, and that is the whole architecture. Neither player ever sees the other's
    /// object, so there is nothing to replicate: no NetworkObject, no ClientNetworkTransform, no
    /// authority to hand around when a grab starts. Both objects exist on both machines and each
    /// client hides the one that is not theirs, exactly as the 真人 are handled in O-0. A later
    /// beat that needs the other player to see what you are holding would need all of that; this
    /// one does not, and building it anyway would mean maintaining a synchronisation nobody can
    /// observe.
    ///
    /// THE LAMP FLOATS, it does not lie on the floor. 「物件出現 浮在箱子正上方」 -- it dissolves in
    /// hanging over the carton, which is what makes the ask legible without a word of
    /// instruction: the thing and the place it goes are in the same glance. So the Rigidbody
    /// ships NON-KINEMATIC WITH GRAVITY OFF -- a body with no forces on it simply stays where it
    /// is -- and <see cref="XRGrabInteractable.forceGravityOnDetach"/> switches gravity on the
    /// first time the player lets go. Kinematic would have floated too, and would have come back
    /// kinematic on release: XRI restores whatever it recorded at the grab, and only the gravity
    /// flag is OR-ed with the force. A lamp that cannot fall cannot be dropped into a box.
    ///
    /// Where it floats is <see cref="BuildO1Box"/>'s business, together with the carton it hangs
    /// over and the floor underneath. Run that after this one.
    ///
    /// Visibility is RoleVisibility, which the project already has -- renderers AND colliders, so
    /// the other player cannot grab a lamp they cannot see by sweeping a hand through it.
    ///
    /// THE LAMP IS FOUR MESHES -- base, stem, collar and shade -- so the model is instantiated
    /// and unpacked rather than rebuilt from a single mesh, and every piece gets its own convex
    /// collider under one Rigidbody on the root. A convex hull of the whole lamp would be a cone
    /// with the shade's rim for a skirt, which is a wide thing to get into a carton.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildO1Grab.Run
    /// </summary>
    public static class BuildO1Grab
    {
        const string k_PrefabPath = "Assets/_Scenes/O_1.prefab";

        /// <summary>
        /// One of O-1's objects: the model both players get, tinted with whichever of the two
        /// 代表色 owns it. 「出現同一個物件，但是不同顏色/樣式」 -- so a prop is one mesh and two colours,
        /// never two meshes.
        ///
        /// THE COLOURS ARE NOT THE PROP'S TO CHOOSE. They were, per prop and by eye -- a warm
        /// and a cold lamp, a rust and a moss jacket -- which gave the room a third and fourth
        /// pair of player colours on top of the 光圈, the silhouettes and the orbits. 「玩家 A、B
        /// 身上的代表色仍然保留」 means an object in your hand is marked with YOUR colour, the same
        /// one it has been since O-0, so the tint comes from
        /// <see cref="PlayerRoleColors"/> and a prop no longer carries one.
        /// </summary>
        public readonly struct Prop
        {
            public readonly string key, model, source, materialA, materialB;
            public readonly float size;

            public Prop(string key, string model, string source, float size)
            {
                this.key = key;
                this.model = model;
                this.source = source;
                this.size = size;
                materialA = $"Assets/Materials/O1{key}A.mat";
                materialB = $"Assets/Materials/O1{key}B.mat";
            }

            /// <summary>The station names, which <see cref="BuildO1Box"/> and the timeline share.</summary>
            public string StationA => $"{key} (Player A)";
            public string StationB => $"{key} (Player B)";

            public string Station(PlayerRole role) =>
                role == PlayerRole.Player1 ? StationA : StationB;

            public string Material(PlayerRole role) =>
                role == PlayerRole.Player1 ? materialA : materialB;

            /// <summary>
            /// The owner's 代表色, straight from <see cref="PlayerRoleColors"/> -- the same value
            /// the 光圈, the silhouette and the orbits use, undimmed and un-blended, so an
            /// object reads as that player's at a glance.
            /// </summary>
            public Color Tint(PlayerRole role) => PlayerRoleColors.For(role);
        }

        /// <summary>
        /// O-1's objects IN THE ORDER THEY APPEAR, which is v4's: 鬼的抓取1 the lamp, 鬼的抓取2 the
        /// jacket. The sheet writes the second one as a T-shirt; the model used is SmallProps'
        /// jacket, which is the garment this project actually has, and 「搶衣服」 reads the same
        /// either way.
        ///
        /// THE ORDER IS THE RUNNING ORDER. <see cref="BuildO1Timeline"/> puts the first one on
        /// the beat's animation track -- it arrives at an authored time -- and hands every later
        /// one to <see cref="CompositeBody.Experience.O1GrabWaves"/>, which brings it in when the
        /// previous object's line has finished. Adding a third here adds a wave.
        ///
        /// THE SIZES ARE REAL METRES along each model's longest axis. A prop that is secretly
        /// four metres tall is the sort of thing that costs an afternoon in a headset.
        /// </summary>
        public static readonly Prop[] Props =
        {
            new Prop("Lamp",
                     "Assets/_models/SmallProps/Lamp.fbx",
                     "Assets/_models/SmallProps/Lamp.mat",
                     0.40f),

            new Prop("Jacket",
                     "Assets/_models/SmallProps/Jacket.fbx",
                     "Assets/_models/SmallProps/Jacket.mat",
                     0.55f),
        };

        /// <summary>The first object's stations, which the carton and the beat's track hang off.</summary>
        public static string StationA => Props[0].StationA;
        public static string StationB => Props[0].StationB;

        /// <summary>
        /// The coat and the mug, by the names they were built under. A prefab from before the
        /// lamp is migrated rather than added to: the station keeps its place, its
        /// RoleVisibility, its O1GrabStation and its Box, and only the prop inside it changes.
        /// </summary>
        static readonly (string from, string to)[] k_Renames =
        {
            ("Coat (Player A)", "Lamp (Player A)"),
            ("Mug (Player B)", "Lamp (Player B)"),
        };

        /// <summary>
        /// Where a station that has never been built starts. Only a seed: <see cref="BuildO1Box"/>
        /// floats every object over its player's carton afterwards, which is where they are
        /// actually seen. These two are the 真人's own marks -- the first object arrives where
        /// the body that left it stood.
        /// </summary>
        static readonly Vector3 k_StartA = new Vector3(-1.144f, 0f, 1.375f);
        static readonly Vector3 k_StartB = new Vector3(1.001f, 0f, 1.375f);

        [MenuItem("Tools/O-1/Build Grab Interaction")]
        public static void Run()
        {
            Debug.Log("[O-1] Starting...");

            var root = new GameObject("O_1");
            var content = new GameObject("Content");
            content.transform.SetParent(root.transform, false);

            if (!BuildInto(content.transform))
            {
                Object.DestroyImmediate(root);
                Debug.LogError("[O-1] RESULT: FAIL - the lamp model or its material is missing. " +
                               "Expected every model and material named in Props: " +
                               $"{string.Join(", ", System.Array.ConvertAll(Props, p => p.model))}.");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(k_PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root, k_PrefabPath, out bool saved);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();

            if (!saved)
            {
                Debug.LogError($"[O-1] RESULT: FAIL - could not write {k_PrefabPath}.");
                return;
            }

            Debug.Log($"[O-1] {k_PrefabPath}\n" +
                      "  Each object in the owner's 代表色 -- A 湖水綠, B 藍色 -- and visible to that " +
                      "player only; renderers and colliders both.\n" +
                      "  Local only -- neither object is networked, because neither is ever seen " +
                      "by the other player.\n" +
                      "  F12 in the debug panel switches which one you see.\n" +
                      "[O-1] RESULT: PASS");
        }

        /// <summary>
        /// Put the two objects under an existing parent, for the merged Onboarding prefab. The
        /// beat is two props and a rule about who sees them; it has no room of its own, which is
        /// the whole reason the beats were merged.
        ///
        /// Returns false if the model or a shader is missing, so a caller building something
        /// larger can stop rather than save a prefab with a hole in it.
        /// </summary>
        public static bool BuildInto(Transform parent)
        {
            foreach ((string from, string to) in k_Renames) Migrate(parent, from, to);

            foreach (Prop prop in Props)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(prop.model);
                var source = AssetDatabase.LoadAssetAtPath<Material>(prop.source);
                if (model == null || source == null)
                {
                    Debug.LogError($"[O-1] missing {(model == null ? prop.model : prop.source)}.");
                    return false;
                }

                foreach ((PlayerRole role, Vector3 start) in new[]
                         {
                             (PlayerRole.Player1, k_StartA),
                             (PlayerRole.Player2, k_StartB),
                         })
                {
                    Material material = BuildMaterial(source, prop.Material(role),
                                                      $"O1{prop.key}{(role == PlayerRole.Player1 ? "A" : "B")}",
                                                      prop.Tint(role));

                    BuildStation(parent, prop, prop.Station(role), model, material, role, start);
                }
            }

            return true;
        }

        /// <summary>
        /// A station built under the old name keeps everything but its prop. The name is what
        /// every other builder in the beat looks it up by, so renaming it IS the migration --
        /// and the coat or the mug inside it is left for <see cref="BuildStation"/> to replace,
        /// which it does by position so the lamp lands where the old prop was staged.
        /// </summary>
        static void Migrate(Transform parent, string from, string to)
        {
            if (parent.Find(to) != null) return;

            Transform old = parent.Find(from);
            if (old == null) return;

            old.name = to;
            Debug.Log($"[O-1] '{from}' renamed to '{to}'; its box, its visibility rule and its " +
                      "staging are kept, and the prop inside it is replaced below.");
        }

        /// <summary>
        /// One object: the lamp, something to grab it by, and the rule about who can see it.
        ///
        /// A RE-RUN REPLACES THE PROP AND KEEPS WHERE IT STOOD. The old builder left an existing
        /// station completely alone, which was right while the prop never changed; it is wrong
        /// now, because the whole point of this run is that the coat and the mug become lamps.
        /// So the prop is rebuilt every time and its position, rotation and the station's own
        /// transform are carried across -- staging survives, the model does not.
        /// </summary>
        static void BuildStation(Transform parent, Prop spec, string name, GameObject model,
                                 Material material, PlayerRole owner, Vector3 fallback)
        {
            Transform station = parent.Find(name);
            if (station == null)
            {
                var made = new GameObject(name);
                made.transform.SetParent(parent, false);
                station = made.transform;
            }

            // Whatever is grabbable under the station is the prop, whatever it is called. The
            // coat was "Coat" and the mug "Mug"; looking for the component rather than a name
            // is what lets this migrate a station it did not build.
            var old = station.GetComponentInChildren<XRGrabInteractable>(true);
            Vector3 at = old != null ? old.transform.position : fallback;
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var prop = (GameObject)PrefabUtility.InstantiatePrefab(model, station);
            PrefabUtility.UnpackPrefabInstance(prop, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);
            prop.name = spec.key;

            // UPRIGHT, AND THE OLD PROP'S ROTATION IS NOT CARRIED ACROSS. The mug was staged
            // lying on its side, and inheriting that turned Player B's lamp on its side too --
            // a shade pointing down the room, hanging over the carton at the wrong height
            // because its bounds were no longer the lamp's own. Position is worth keeping
            // across a migration; the angle a different object happened to be at is not, and a
            // lamp has exactly one way up.
            prop.transform.localRotation = Quaternion.identity;

            // Scaled to a real size from the model's own bounds, so it does not matter what units
            // it was exported in -- and the factor is logged, so a model that turns out to be in
            // millimetres says so rather than merely looking wrong.
            Bounds local = Measure(prop.transform);
            float longest = Mathf.Max(local.size.x, Mathf.Max(local.size.y, local.size.z));
            float scale = longest > 1e-5f ? spec.size / longest : 1f;
            prop.transform.localScale = Vector3.one * scale;

            // THE MODEL'S PIVOT IS NOT UNDER THE LAMP. Its meshes sit 38 cm off the FBX's own
            // origin, so a transform put at a position would stand the lamp most of a metre away
            // from it. The object is moved by the difference between where its bounds ended up
            // and where they were asked to be, which is also how the carton is centred.
            prop.transform.position = at;
            Bounds world = Measure(prop.transform);
            prop.transform.position += new Vector3(at.x - world.center.x,
                                                   at.y - world.min.y,
                                                   at.z - world.center.z);

            foreach (Renderer r in prop.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterial = material;

            // One convex collider per piece under one Rigidbody on the root: a compound body,
            // which is what Unity wants for a shape that is not a single hull. Convex because
            // the body is not kinematic -- and the shade being a little fatter than it really is
            // is the forgiving direction to be wrong in for something meant to be picked up.
            foreach (MeshFilter filter in prop.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var collider = filter.gameObject.GetComponent<MeshCollider>();
                if (collider == null) collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = true;
            }

            var rb = prop.GetComponent<Rigidbody>();
            if (rb == null) rb = prop.AddComponent<Rigidbody>();
            rb.mass = 1f;

            // FLOATING, AND NOT KINEMATIC. 「浮在箱子正上方」 -- a non-kinematic body with gravity
            // off has no forces on it and stays exactly where it was put, which is the float;
            // forceGravityOnDetach below turns gravity on the first time the player lets go, and
            // XRI's restore-on-release only ever OR-s that flag, never clears it. A kinematic
            // body would hover too and would come back kinematic on release, leaving a lamp that
            // hangs wherever the player's hand opened.
            rb.useGravity = false;
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var grab = prop.GetComponent<XRGrabInteractable>();
            if (grab == null) grab = prop.AddComponent<XRGrabInteractable>();
            grab.useDynamicAttach = true;        // held where the hand closed, not snapped to the pivot
            grab.throwOnDetach = false;          // these are meant to be put down, not thrown
            grab.forceGravityOnDetach = true;    // and once put down, to fall

            // On the station rather than on the object, so the rule survives the object being
            // rebuilt, and so the whole station goes together if anything is added beside it.
            var visibility = station.GetComponent<RoleVisibility>();
            if (visibility == null) visibility = station.gameObject.AddComponent<RoleVisibility>();
            var so = new SerializedObject(visibility);
            SerializedProperty roles = so.FindProperty("m_VisibleToRoles");
            roles.arraySize = 1;
            roles.GetArrayElementAtIndex(0).intValue = (int)owner;
            so.FindProperty("m_AlsoDisableColliders").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[O-1] {name}: {spec.key} " +
                      $"{local.size.x:F2}x{local.size.y:F2}x{local.size.z:F2} -> scale " +
                      $"{scale:F4} for {spec.size:F2} m, {material.name} tinted " +
                      $"{ColorUtility.ToHtmlStringRGB(material.GetColor("_BaseColor"))} " +
                      $"({owner}'s 代表色), visible to {owner} only, floating until it is let go.");
        }

        /// <summary>Every renderer under an object, in one box, in world space.</summary>
        static Bounds Measure(Transform prop)
        {
            var renderers = prop.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(prop.position, Vector3.zero);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        /// <summary>
        /// The lamp's own material, copied and tinted, and made transparent.
        ///
        /// Copied rather than written out property by property, so whatever the model came
        /// textured with -- base, normal, metallic -- is what the room shows; the tint and the
        /// surface mode are this project's business and nothing else is.
        ///
        /// LEFT OPAQUE, and the dissolve done by clipping it. The alternative -- alpha blending,
        /// which is what this did for a while -- is what put these objects behind the water: see
        /// the remarks in the body. <see cref="BuildO1Timeline"/> turns the result into an
        /// alpha-clipped material afterwards, the way the cartons already were.
        /// </summary>
        static Material BuildMaterial(Material source, string path, string name, Color tint)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source.shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = source.shader;
            material.CopyPropertiesFromMaterial(source);
            material.shaderKeywords = source.shaderKeywords;
            material.name = name;

            // The tint multiplies the base map, and its alpha is the presence PropPresence
            // writes -- so it goes in at 1 and the component takes it from there.
            tint.a = 1f;
            material.SetColor("_BaseColor", tint);

            // AND IT STAYS OPAQUE. This used to be made alpha-blended here so PropPresence
            // could fade it, and that is what made these objects disappear behind the water:
            // KWS draws the room's water through passes that assume the scene is in the depth
            // buffer and in _CameraOpaqueTexture, and an alpha-blended material is in neither.
            // A lamp dragged in straight from SmallProps -- the same mesh, the same base map,
            // opaque -- is visible in the same frame the built one is not.
            //
            // So the dissolve is a CLIP, not a fade, exactly as the cartons beside it already
            // do it: BuildO1Timeline.MakePropClippable writes noise into a copy of the base
            // map's alpha and PropPresence drives _Cutoff against it. Which is why nothing here
            // touches _Surface, the blend modes or the queue any more -- the material this
            // method returns is still the model's own opaque one, only tinted.

            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
