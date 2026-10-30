using UnityEditor;
using UnityEngine;
using CompositeBody.Experience;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Two lights circling the origin on two paths, one purple and one yellow, each dragging a
    /// trail that sheds particles.
    ///
    /// THE PATHS ARE DELIBERATELY UNEQUAL, and in three ways at once, because changing only one
    /// reads as a mistake rather than as intent: the radii differ point to point, the control
    /// points are not evenly spaced around the circle, and the heights rise and fall on a
    /// different count from the radii so the two never line up. A circle with a wobble still
    /// reads as a circle; this does not.
    ///
    /// The two paths are NOT mirror images. Mirroring is the symmetry people actually notice --
    /// two lights crossing at the same two points every lap, forever. Different point counts
    /// (7 and 5) and different lap times (48 s and 61 s) mean the crossings move, and because
    /// 48 and 61 share no factor the pair does not repeat for the length of the piece.
    ///
    /// Built into the Onboarding prefab beside the beats. Re-running leaves an existing orbit
    /// alone -- once these are shaped by hand, that shaping is the work.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildLightOrbits.Run
    /// </summary>
    public static class BuildLightOrbits
    {
        const string k_PrefabPath = "Assets/_Scenes/Onboarding.prefab";
        const string k_TrailMaterial = "Assets/Materials/OrbitTrail.mat";
        const string k_GlowMaterial = "Assets/Materials/OrbitGlow.mat";
        const string k_SparkMaterial = "Assets/Materials/OrbitSpark.mat";

        [MenuItem("Tools/Onboarding/Build Light Orbits")]
        public static void Run() => Build(replace: false);

        /// <summary>
        /// Throw away the existing orbits and build them again. Separate menu item, and not the
        /// default, because the paths are meant to be shaped by hand once they exist -- a
        /// builder that silently replaced that shaping would be a builder nobody dared run.
        /// </summary>
        [MenuItem("Tools/Onboarding/Rebuild Light Orbits (discards hand-shaped paths)")]
        public static void Rebuild() => Build(replace: true);

        static void Build(bool replace)
        {
            Debug.Log($"[Orbits] Starting ({(replace ? "rebuild" : "create if missing")})...");

            GameObject root = PrefabUtility.LoadPrefabContents(k_PrefabPath);
            if (root == null)
            {
                Debug.LogError($"[Orbits] RESULT: FAIL - could not open {k_PrefabPath}.");
                return;
            }

            try
            {
                Transform content = root.transform.Find("Content");
                if (content == null)
                {
                    Debug.LogError("[Orbits] RESULT: FAIL - no Content in the prefab.");
                    return;
                }

                Transform orbits = content.Find("Orbits");
                if (orbits == null)
                {
                    var go = new GameObject("Orbits");
                    go.transform.SetParent(content, false);
                    orbits = go.transform;
                }

                if (replace)
                {
                    foreach (string name in new[] { "Orbit Purple", "Orbit Yellow" })
                    {
                        Transform old = orbits.Find(name);
                        if (old != null)
                        {
                            Object.DestroyImmediate(old.gameObject);
                            Debug.Log($"[Orbits] {name}: removed for rebuild.");
                        }
                    }
                }

                Material trailMat = LoadOrCreateTrailMaterial();
                Material sparkMat = LoadOrCreateSparkMaterial();

                // Player A, purple. Seven points, swinging wide on one side and tucking in on the
                // other, and never the same height twice in a row.
                BuildOrbit(orbits, "Orbit Purple", PlayerRoleColors.Player1, trailMat, sparkMat,
                           seconds: 48f, phase: 0f, reverse: false,
                           radii: new[] { 2.35f, 1.62f, 2.05f, 2.80f, 1.48f, 2.20f, 1.85f },
                           angles: new[] { 0f, 44f, 97f, 158f, 203f, 268f, 316f },
                           heights: new[] { 2.05f, 2.55f, 1.80f, 2.30f, 2.70f, 1.95f, 2.40f });

                // Player B, yellow. Five points, a lap long enough that it never keeps step with
                // the purple one, and running the other way so they meet head-on rather than
                // chasing each other round.
                BuildOrbit(orbits, "Orbit Yellow", PlayerRoleColors.Player2, trailMat, sparkMat,
                           seconds: 61f, phase: 0.37f, reverse: true,
                           radii: new[] { 1.75f, 2.60f, 1.40f, 2.95f, 2.10f },
                           angles: new[] { 22f, 86f, 171f, 241f, 303f },
                           heights: new[] { 2.60f, 1.85f, 2.35f, 2.80f, 2.10f });

                PrefabUtility.SaveAsPrefabAsset(root, k_PrefabPath);
                Debug.Log("[Orbits] RESULT: PASS");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void BuildOrbit(Transform parent, string name, Color colour,
                               Material trailMat, Material sparkMat,
                               float seconds, float phase, bool reverse,
                               float[] radii, float[] angles, float[] heights)
        {
            if (parent.Find(name) != null)
            {
                Debug.Log($"[Orbits] {name}: already present, left as it is.");
                return;
            }

            var orbit = new GameObject(name);
            orbit.transform.SetParent(parent, false);
            orbit.transform.localPosition = Vector3.zero;   // circling 0,0,0

            // The mover first, so the component can be told about it before the points exist and
            // the auto-collect never picks it up as one.
            var moverGO = new GameObject("Light");
            moverGO.transform.SetParent(orbit.transform, false);

            var light = moverGO.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = colour;
            light.intensity = 1.9f;
            light.range = 6.5f;
            light.shadows = LightShadows.None;   // a moving shadow-caster in a point cloud is cost
                                                 // for something nothing in this room would show

            BuildGlow(moverGO, colour, light.range);
            BuildTrail(moverGO, colour, trailMat);
            BuildSparks(moverGO, colour, sparkMat);

            var points = new Transform[radii.Length];
            for (int i = 0; i < radii.Length; i++)
            {
                var p = new GameObject($"P{i}");
                p.transform.SetParent(orbit.transform, false);

                float rad = angles[i] * Mathf.Deg2Rad;
                p.transform.localPosition = new Vector3(Mathf.Cos(rad) * radii[i],
                                                        heights[i],
                                                        Mathf.Sin(rad) * radii[i]);
                points[i] = p.transform;
            }

            var orbitComponent = orbit.AddComponent<LightOrbit>();
            var so = new SerializedObject(orbitComponent);
            so.FindProperty("m_Mover").objectReferenceValue = moverGO.transform;
            so.FindProperty("m_Seconds").floatValue = seconds;
            so.FindProperty("m_Phase").floatValue = phase;
            so.FindProperty("m_Reverse").boolValue = reverse;

            SerializedProperty list = so.FindProperty("m_Points");
            list.arraySize = points.Length;
            for (int i = 0; i < points.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            orbitComponent.Rebuild();

            Debug.Log($"[Orbits] {name}: {points.Length} points, {seconds:F0}s lap, " +
                      $"{(reverse ? "reversed" : "forward")}, path {orbitComponent.length:F2} m.");
        }

        /// <summary>Additive sprites for the sparks. Separate from the tube, which needs width.</summary>
        static Material LoadOrCreateSparkMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(k_SparkMaterial);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                            ?? Shader.Find("Sprites/Default");

            var material = new Material(shader) { name = "OrbitSpark" };
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 1f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            AssetDatabase.CreateAsset(material, k_SparkMaterial);
            Debug.Log($"[Orbits] created {k_SparkMaterial}.");
            return material;
        }

        /// <summary>
        /// The air around the light, which is what stops it reading as a flat dot.
        ///
        /// A sphere proxy with CompositeBody/VolumetricPoint on it, which solves the air-light
        /// integral in closed form rather than marching -- see the shader for why that choice is
        /// about these lights MOVING rather than about cost.
        ///
        /// Built at half the light's range. The glow wants to be smaller than the illumination:
        /// a volume as large as the falloff is mostly very faint pixels, and overdraw on a full
        /// sphere is the one thing this technique is expensive at.
        /// </summary>
        static void BuildGlow(GameObject owner, Color colour, float lightRange)
        {
            Shader shader = Shader.Find("CompositeBody/VolumetricPoint");
            if (shader == null)
            {
                Debug.LogWarning("[Orbits] CompositeBody/VolumetricPoint not found; the light " +
                                 "will have no volume around it.");
                return;
            }

            var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glow.name = "Glow";
            glow.transform.SetParent(owner.transform, false);
            Object.DestroyImmediate(glow.GetComponent<Collider>());   // nothing may touch the air

            var material = LoadOrCreate(k_GlowMaterial, shader, "OrbitGlow");
            var renderer = glow.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            var volume = glow.AddComponent<VolumetricPoint>();
            var so = new SerializedObject(volume);
            // A quarter of the light's range, not half. The first pass used half and the glow
            // swallowed the room: a 3 m ball of light in a 5 m space is not a light with air
            // around it, it is weather. The volume should read as the air immediately about the
            // source while the Light component does the reach.
            so.FindProperty("m_Range").floatValue = lightRange * 0.25f;
            so.FindProperty("m_Color").colorValue = colour;
            so.FindProperty("m_Intensity").floatValue = 0.55f;
            so.FindProperty("m_Density").floatValue = 0.5f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Material LoadOrCreate(string path, Shader shader, string name)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
            Debug.Log($"[Orbits] created {path}.");
            return material;
        }

        /// <summary>
        /// The trail itself: a short ribbon that fades out along its length, so the light reads as
        /// having just come from somewhere rather than as towing a solid rope.
        /// </summary>
        static void BuildTrail(GameObject owner, Color colour, Material material)
        {
            var trail = owner.AddComponent<TrailRenderer>();
            trail.time = 4.0f;
            trail.startWidth = 0.13f;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.03f;
            trail.sharedMaterial = material;
            // View alignment is what made this read as a decal: a strip that always faces you
            // has no thickness to see. The tube material shades it with a radial falloff across
            // its width and a soft depth intersection, so the same geometry reads as a cylinder
            // of lit air. Widened to suit, since a tube one pixel across is still a line.
            trail.alignment = LineAlignment.View;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.numCapVertices = 2;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(colour, 0f), new GradientColorKey(colour, 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;
        }

        /// <summary>
        /// Particles shed from the trail. Emitted OVER DISTANCE rather than over time, so the
        /// light leaves the same density of sparks however fast that stretch of path happens to
        /// be -- and they are given a little drift and no gravity, so they hang where they were
        /// dropped like the dust already in this room rather than falling.
        /// </summary>
        static void BuildSparks(GameObject owner, Color colour, Material material)
        {
            var sparksGO = new GameObject("Sparks");
            sparksGO.transform.SetParent(owner.transform, false);

            var ps = sparksGO.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = 4.5f;
            main.startSpeed = 0.04f;
            main.startSize = 0.016f;
            main.startColor = colour;
            main.gravityModifier = 0f;
            main.maxParticles = 600;
            main.simulationSpace = ParticleSystemSimulationSpace.World;  // left behind, not carried
            main.playOnAwake = true;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 14f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.035f;

            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(colour, 0f), new GradientColorKey(colour, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.7f, 0.15f),
                        new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));

            var renderer = sparksGO.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>
        /// One additive material for both the trails and the sparks. Additive because this room
        /// is nearly black and these are light rather than objects -- and because the colour per
        /// orbit comes from the gradients, one material serves both lights.
        /// </summary>
        static Material LoadOrCreateTrailMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(k_TrailMaterial);
            if (existing != null) return existing;

            // The tube shader if it is there -- it shades the ribbon as a cylinder, which is the
            // difference between a trail with depth and a sticker. The particle shader is the
            // fallback, and the sparks use it regardless since a point sprite has no width to
            // take a chord across.
            Shader tube = Shader.Find("CompositeBody/VolumetricTube");
            if (tube != null)
            {
                var tubeMat = new Material(tube) { name = "OrbitTrail" };
                AssetDatabase.CreateAsset(tubeMat, k_TrailMaterial);
                Debug.Log($"[Orbits] created {k_TrailMaterial} on VolumetricTube.");
                return tubeMat;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogWarning("[Orbits] URP particle shader not found; falling back to Sprites.");
                shader = Shader.Find("Sprites/Default");
            }

            var material = new Material(shader) { name = "OrbitTrail" };
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);   // transparent
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 1f);       // additive
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            AssetDatabase.CreateAsset(material, k_TrailMaterial);
            Debug.Log($"[Orbits] created {k_TrailMaterial}.");
            return material;
        }
    }
}
