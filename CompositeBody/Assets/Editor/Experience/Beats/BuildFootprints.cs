using System.IO;
using UnityEditor;
using UnityEngine;
using CompositeBody.Experience;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Two rings of footprints on the floor, one per player, in their own 代表色. The timeline
    /// switches them on; <see cref="FootprintRing"/> lays them down one at a time.
    ///
    /// THE PRINT IS GENERATED, NOT IMPORTED. A footprint is a silhouette -- a heel, an arch, a
    /// ball and five toes -- and writing it as a function is both shorter than sourcing art and
    /// exactly reproducible: the texture can be deleted and rebuilt without anyone having to find
    /// the original file again. It is symmetric on purpose, so one texture serves both feet; a
    /// handed print would need a mirrored quad, and a quad with a negative scale is back-face
    /// culled and simply vanishes.
    ///
    /// CONCENTRIC RINGS, NOT ONE. The room is 4x5 m and both players walk the circle at the same
    /// time; one line of prints in the dark is two people meeting on it. Player A takes the inner
    /// ring and starts opposite Player B.
    ///
    /// Re-running leaves existing rings alone, because where a print sits is a staging decision.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildFootprints.Run
    /// </summary>
    public static class BuildFootprints
    {
        const string k_PrefabPath = "Assets/_Scenes/Onboarding.prefab";
        const string k_TexturePath = "Assets/Textures/Footprint.png";
        const string k_MaterialPath = "Assets/Materials/Footprint.mat";
        const string k_RootName = "Footprints";

        // 1:2.25, close to the proportions of the foot drawn in it, so the quad below stretches
        // it almost not at all.
        const int k_TexWidth = 128;
        const int k_TexHeight = 288;

        /// <summary>Prints per ring. 12 is a step every 30 degrees, which is a walkable stride.</summary>
        const int k_Count = 12;

        const float k_InnerRadius = 1.2f;
        const float k_OuterRadius = 1.6f;

        /// <summary>
        /// A foot, in metres -- the QUAD's size, not the print's. The foot drawn inside it now
        /// fills the frame, so these are very nearly the print's own dimensions: about 9 cm
        /// across and 21 cm long.
        ///
        /// SHORTER AND MUCH WIDER THAN THE FIRST VERSION. The silhouette used to occupy two
        /// fifths of the texture's width, so a 26 cm quad drew a foot 4.6 cm across -- a long
        /// thin smear rather than a footprint. Widening the drawing was most of the fix; the
        /// quad is shorter as well because a print at a walking scale reads better slightly
        /// under life size.
        /// </summary>
        const float k_PrintLength = 0.22f;
        const float k_PrintWidth = 0.10f;

        /// <summary>Metres to each side of the walking line, so the prints alternate feet.</summary>
        const float k_Stride = 0.08f;

        /// <summary>Above the floor, enough to beat z-fighting with it.</summary>
        const float k_Lift = 0.012f;

        /// <summary>
        /// Write the print texture again, over the same file. In place, so the asset keeps its
        /// GUID and the material does not lose its reference -- deleting and recreating it would
        /// leave the prints invisible until someone noticed and reassigned the texture by hand.
        /// </summary>
        [MenuItem("Tools/O-1/Rebuild Footprint Texture")]
        public static void RebuildTexture()
        {
            Debug.Log("[Footprints] rebuilding the print texture...");
            WriteTexture();
            AssetDatabase.SaveAssets();
            Debug.Log("[Footprints] RESULT: PASS");
        }

        /// <summary>
        /// Throw the rings away and build them again. A separate menu item, and not the default,
        /// because where a print sits is a staging decision and a builder that silently replaced
        /// it is a builder nobody dares run -- the same split the orbit builder has.
        /// </summary>
        [MenuItem("Tools/O-1/Rebuild Footprints (discards hand placement)")]
        public static void Rebuild() => Build(replace: true);

        [MenuItem("Tools/O-1/Build Footprints")]
        public static void Run() => Build(replace: false);

        static void Build(bool replace)
        {
            Debug.Log("[Footprints] Starting...");

            GameObject root = PrefabUtility.LoadPrefabContents(k_PrefabPath);
            if (root == null)
            {
                Debug.LogError($"[Footprints] RESULT: FAIL - could not open {k_PrefabPath}.");
                return;
            }

            try
            {
                Transform content = root.transform.Find("Content");
                if (content == null)
                {
                    Debug.LogError("[Footprints] RESULT: FAIL - no Content in the prefab.");
                    return;
                }

                if (replace)
                {
                    Transform old = content.Find(k_RootName);
                    if (old != null)
                    {
                        Object.DestroyImmediate(old.gameObject);
                        Debug.Log("[Footprints] removed the existing rings for a rebuild.");
                        Debug.LogWarning("[Footprints] the rings are NEW OBJECTS, so the show " +
                                         "timeline's activation track is now bound to nothing " +
                                         "and the prints would be visible from the start of the " +
                                         "show. Re-run Tools/Show/Build Main Timeline.");
                    }
                }

                if (!BuildInto(content)) return;

                PrefabUtility.SaveAsPrefabAsset(root, k_PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[Footprints] RESULT: PASS");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Put the rings under an existing parent. Returns false only if the material could not
        /// be made, which is the one failure that would leave invisible prints.
        /// </summary>
        public static bool BuildInto(Transform content)
        {
            Material material = LoadOrCreateMaterial();
            if (material == null)
            {
                Debug.LogError("[Footprints] RESULT: FAIL - no material; the prints would be invisible.");
                return false;
            }

            Transform existingRoot = content.Find(k_RootName);
            if (existingRoot != null)
            {
                Debug.Log($"[Footprints] '{k_RootName}' already present, left as it is.");
                return true;
            }

            var footprints = new GameObject(k_RootName);
            footprints.transform.SetParent(content, false);

            BuildRing(footprints.transform, "Ring Player A", PlayerRole.Player1,
                      k_InnerRadius, startDegrees: 0f, material);
            BuildRing(footprints.transform, "Ring Player B", PlayerRole.Player2,
                      k_OuterRadius, startDegrees: 180f, material);

            // LEFT ON, and the timeline is what hides it. An activation track forces its target
            // inactive everywhere it has no clip, so the prints are off from the first evaluated
            // frame of the show until the payoff -- without the group having to be off in the
            // prefab, where it would be invisible to author and its components' Awake would
            // never run to register a debug row.
            Debug.Log($"[Footprints] built 2 rings of {k_Count} at {k_InnerRadius} m and " +
                      $"{k_OuterRadius} m; the timeline hides them until O-1 is paid off.");
            return true;
        }

        static void BuildRing(Transform parent, string name, PlayerRole role, float radius,
                              float startDegrees, Material material)
        {
            var ring = new GameObject(name);
            ring.transform.SetParent(parent, false);

            for (int i = 0; i < k_Count; i++)
            {
                float t = (float)i / k_Count;
                float angle = (startDegrees + t * 360f) * Mathf.Deg2Rad;

                Vector3 radial = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                // Anticlockwise seen from above, which is the direction the tangent below points.
                Vector3 tangent = new(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));

                // Alternating feet: half a stride each side of the walking line, which is radial
                // here because the line itself is the circle.
                float side = (i % 2 == 0) ? k_Stride : -k_Stride;

                var print = GameObject.CreatePrimitive(PrimitiveType.Quad);
                print.name = $"Print {i:D2} {(i % 2 == 0 ? "R" : "L")}";
                print.transform.SetParent(ring.transform, false);
                print.transform.position = radial * radius + radial * side + Vector3.up * k_Lift;

                // DOWN, NOT UP, and the sign is the whole of it: a Unity Quad's normal is its
                // local -Z, so aiming local +Z at the ceiling puts the visible face into the
                // floor. Every print was there, lit and enabled, and rendered as nothing at all.
                // Aiming +Z down turns the face up; the second argument still points the print's
                // own up-axis along the way it is walking.
                print.transform.rotation = Quaternion.LookRotation(Vector3.down, tangent);
                // MIRRORED FOR EVERY OTHER STEP, so the prints are left and right feet rather
                // than one foot stamped twelve times. A negative scale flips the winding and
                // would be back-face culled away -- which is exactly why the material is drawn
                // from both sides, and why this is affordable at all.
                float handed = (i % 2 == 0) ? k_PrintWidth : -k_PrintWidth;
                print.transform.localScale = new Vector3(handed, k_PrintLength, 1f);

                Object.DestroyImmediate(print.GetComponent<Collider>());

                var renderer = print.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            }

            var component = ring.AddComponent<FootprintRing>();
            var so = new SerializedObject(component);
            so.FindProperty("m_Role").intValue = (int)role;

            SerializedProperty prints = so.FindProperty("m_Prints");
            prints.arraySize = k_Count;
            for (int i = 0; i < k_Count; i++)
            {
                prints.GetArrayElementAtIndex(i).objectReferenceValue =
                    ring.transform.GetChild(i).GetComponent<MeshRenderer>();
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[Footprints] {name}: {k_Count} prints at {radius:F2} m, " +
                      $"starting at {startDegrees:F0}deg, {role}.");
        }

        /// <summary>
        /// Additive and unlit, like the orbit trails: these are light on a floor in a nearly
        /// black room, not paint. The colour comes from a property block per ring, so one
        /// material serves both.
        /// </summary>
        static Material LoadOrCreateMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(k_MaterialPath);
            if (existing != null)
            {
                if (existing.GetTexture("_BaseMap") == null)
                    existing.SetTexture("_BaseMap", LoadOrCreateTexture());
                ConfigureBlend(existing);
                MakeDoubleSided(existing);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogError("[Footprints] no URP Unlit shader.");
                return null;
            }

            var material = new Material(shader) { name = "Footprint" };
            material.SetTexture("_BaseMap", LoadOrCreateTexture());
            ConfigureBlend(material);
            MakeDoubleSided(material);

            AssetDatabase.CreateAsset(material, k_MaterialPath);
            Debug.Log($"[Footprints] created {k_MaterialPath}.");
            return material;
        }

        /// <summary>
        /// Transparent and ADDITIVE, set through URP's own _Blend mode rather than by writing
        /// _SrcBlend and _DstBlend directly.
        ///
        /// WRITING THE BLEND FACTORS BY HAND DOES NOT SURVIVE. URP revalidates a material when it
        /// is imported and rewrites both factors from _Surface and _Blend -- so a material that
        /// asked for SrcAlpha/One and left _Blend at Premultiply came back as One/OneMinusSrcAlpha,
        /// which adds the texture's white at full strength even where its alpha is zero. Every
        /// print rendered as a solid rectangle with a footprint faintly inside it.
        ///
        /// _Blend: 0 alpha, 1 premultiply, 2 additive, 3 multiply.
        /// </summary>
        static void ConfigureBlend(Material material)
        {
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);   // transparent
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 2f);       // additive

            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        /// <summary>
        /// Drawn from both sides. A print lying on the floor is never seen from underneath, so
        /// this buys nothing in the show -- it buys that a print turned the wrong way by hand
        /// later is a print facing the wrong way rather than a print that has vanished, which is
        /// a far harder thing to notice.
        /// </summary>
        static void MakeDoubleSided(Material material)
        {
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            material.doubleSidedGI = true;
        }

        static Texture2D LoadOrCreateTexture()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(k_TexturePath);
            return existing != null ? existing : WriteTexture();
        }

        static Texture2D WriteTexture()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(k_TexturePath));

            var texture = new Texture2D(k_TexWidth, k_TexHeight, TextureFormat.RGBA32, false);
            var pixels = new Color32[k_TexWidth * k_TexHeight];

            for (int y = 0; y < k_TexHeight; y++)
            {
                float v = (y + 0.5f) / k_TexHeight;
                for (int x = 0; x < k_TexWidth; x++)
                {
                    float u = (x + 0.5f) / k_TexWidth;
                    // PREMULTIPLIED: the colour carries the shape as well as the alpha does. If
                    // the material ever comes back from an import on premultiplied blending --
                    // which is what URP does by default -- a white-everywhere texture draws its
                    // whole rectangle, while this one still draws a foot.
                    byte a = (byte)(Mathf.Clamp01(Silhouette(u, v)) * 255f);
                    pixels[y * k_TexWidth + x] = new Color32(a, a, a, a);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            File.WriteAllBytes(k_TexturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(k_TexturePath, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(k_TexturePath);
            if (importer != null)
            {
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;

                // NO MIPMAPS. These are read from a metre or two away at a grazing angle, which
                // is where mip selection drops to a level that has averaged the foot into the
                // transparent surround -- giving the whole quad a low alpha everywhere, which
                // under premultiplied blending darkens the floor and draws the print's rectangle
                // as a banded grey box around it.
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            Debug.Log($"[Footprints] generated {k_TexturePath} ({k_TexWidth}x{k_TexHeight}).");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(k_TexturePath);
        }

        /// <summary>
        /// A foot, as coverage from 0 to 1 at a point in the texture: a sole whose WIDTH VARIES
        /// WITH HEIGHT, plus five toes above it.
        ///
        /// A PROFILE RATHER THAN A UNION OF ELLIPSES, which is what this was first. Three soft
        /// ellipses for heel, arch and ball do not join: at the seam between two of them both are
        /// at their own fading edge, so both contribute nearly nothing and the print comes out as
        /// three separate blobs stacked up like a snowman. One outline that narrows at the arch
        /// cannot come apart, because there is only ever one shape.
        ///
        /// Soft-edged, because a hard edge on a floor seen at a glancing angle aliases into a row
        /// of dashes.
        /// </summary>
        static float Silhouette(float u, float v)
        {
            float a = Sole(u, v);

            // Toes, just clear of the sole: a barefoot print has them detached, and that gap is
            // most of what makes a shape read as a foot rather than as a lozenge.
            //
            // A RIGHT FOOT, so they are not all the same size and not evenly spaced -- a big toe
            // and four that fall away. Five identical dots in a row read as a comb, which is what
            // the symmetric version looked like. The builder mirrors alternate prints to get the
            // left foot, which it can now that the material is drawn from both sides.
            float[] at = { 0.235f, 0.415f, 0.555f, 0.678f, 0.786f };
            float[] rx = { 0.115f, 0.086f, 0.076f, 0.067f, 0.058f };
            float[] cy = { 0.855f, 0.890f, 0.880f, 0.858f, 0.834f };
            for (int i = 0; i < at.Length; i++)
                a = Mathf.Max(a, Ellipse(u, v, at[i], cy[i], rx[i], rx[i] * 1.15f));

            return a;
        }

        /// <summary>Heel to ball, as one outline.</summary>
        static float Sole(float u, float v)
        {
            // The sole ends lower than it used to so the toes have room to be the size they
            // should be: five small dots under a long sole read as a mitten, and the big toe is
            // most of what says this is a foot.
            const float bottom = 0.045f, top = 0.700f;
            if (v < bottom || v > top) return 0f;

            float t = Mathf.InverseLerp(bottom, top, v);
            float half = Profile(t);

            // Rounds both ends off. Flat across the middle and falling away only in the last
            // tenth, so the heel and the ball are domed rather than cut off square.
            half *= Mathf.Sqrt(Mathf.Clamp01(1f - Mathf.Pow(Mathf.Abs(2f * t - 1f), 8f)));
            if (half <= 1e-4f) return 0f;

            // A tight falloff. Wide enough to stop the edge aliasing on a floor seen end-on,
            // narrow enough that the print has an outline rather than being a smudge.
            return Fade(0.86f, 1f, Mathf.Abs(u - 0.5f) / half);
        }

        /// <summary>
        /// 1 at or inside <paramref name="solid"/>, 0 at or beyond <paramref name="gone"/>, eased
        /// between.
        ///
        /// WRITTEN OUT BECAUSE Mathf.SmoothStep IS NOT THE SHADER FUNCTION OF THAT NAME. It
        /// interpolates BETWEEN its first two arguments -- SmoothStep(1, 0.86, x) returns a
        /// number between 1 and 0.86 and never approaches zero. The first version of this
        /// generator used it as an edge test, which gave every print an alpha floor of 0.86: a
        /// glowing rectangle of texture with a slightly brighter foot inside it.
        /// </summary>
        static float Fade(float solid, float gone, float x)
        {
            float t = Mathf.Clamp01((gone - x) / Mathf.Max(gone - solid, 1e-5f));
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Half the sole's width at a height up the foot, 0 at the heel and 1 at the toe line.
        /// Widest at the ball, pinched at the arch, in between at the heel -- which is the one
        /// thing that makes a footprint look like a footprint.
        /// </summary>
        static float Profile(float t)
        {
            // A HEEL IS NARROWER THAN A BALL, by about a quarter, and the arch pinch is brief.
            // With the two ends the same width and a long waist between them the print came out
            // as an hourglass -- symmetrical, and so pointing in neither direction, which is the
            // one thing these have to do.
            // The arch is a NARROWING, not a waist. Pinched to half the ball's width it read as
            // a bone -- two blobs joined by a stick -- which is what a silhouette does when the
            // thin part is both too thin and too long.
            //
            // THE NUMBERS FILL THE FRAME. At a ball half-width of 0.21 the foot used two fifths
            // of the texture and the rest was empty margin, which is what made a life-sized quad
            // draw a print narrow enough to read as a streak.
            float[] at = { 0.00f, 0.15f, 0.34f, 0.48f, 0.63f, 0.79f, 1.00f };
            float[] half = { 0.260f, 0.292f, 0.278f, 0.262f, 0.372f, 0.430f, 0.392f };

            for (int i = 1; i < at.Length; i++)
            {
                if (t > at[i]) continue;
                float k = Mathf.InverseLerp(at[i - 1], at[i], t);
                return Mathf.Lerp(half[i - 1], half[i], Mathf.SmoothStep(0f, 1f, k));
            }
            return half[^1];
        }

        static float Ellipse(float u, float v, float cx, float cy, float rx, float ry)
        {
            float dx = (u - cx) / rx;
            float dy = (v - cy) / ry;
            float d = dx * dx + dy * dy;

            // 1 well inside, 0 outside, with the fade across the last fifth of the radius.
            return Fade(0.64f, 1f, d);
        }
    }
}
