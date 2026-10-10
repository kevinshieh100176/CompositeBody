using System.IO;
using UnityEditor;
using UnityEngine;
using CompositeBody.Experience;
using CompositeBody.Multiplayer;
using CompositeBody.PointClouds;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Authors the contents of O-0 進入 into the beat's content root: the black, the single white
    /// light the players arrive in, 真人 A and B, their two 光圈, the sparse motes, the distant
    /// sound bed and the other player's blob.
    ///
    /// THE SEA IS GONE. V.1 opened on a 64 m displaced water grid and a drift of 「框」; V.2
    /// replaced that with 「全灰平面」 and the two coloured 光圈, and the production timeline is
    /// the authority over the older script. The sea shader and its material are still in the
    /// project -- nothing else needs them yet, but a 38-minute piece with S-acts still unbuilt
    /// is no place to delete a working water surface from.
    ///
    /// THE OPENING IMAGE is one white light in a black room: a wide soft downlight, a slab
    /// standing in it, and the two players inside the same pool. The cue that follows is the
    /// beat -- see <see cref="CompositeBody.Experience.O0ArrivalBeat"/>, which brings the purple
    /// and yellow 光圈 up on each 真人 and then takes the white one away, so the shared light the
    /// two of them arrived in becomes two separate ones. That is the whole piece in thirty
    /// seconds, which is why it is the first thing either player sees.
    ///
    /// All of the beat's layers are built here together rather than one system at a time. The
    /// point of a vertical slice is that the beat is finishable: it is better to have O-0
    /// complete and crude than to have the light, the figures and the audio each half-built
    /// across every beat at once.
    /// </summary>
    public static class BuildO0Arrival
    {
        const string k_BlobMaterialPath = "Assets/Materials/RoleBlob.mat";
        const string k_DustAssetPath = "Assets/VFX/O0Dust.vfx";
        const string k_SlabMaterialPath = "Assets/Materials/O0Slab.mat";
        const string k_FloorMaterialPath = "Assets/Materials/O0Floor.mat";
        const string k_SpotWhitePath = "Assets/Materials/O0SpotWhite.mat";
        const string k_SpotColourPath = "Assets/Materials/O0SpotColour.mat";
        const string k_AudioDir = "Assets/Audio/Placeholder";
        const string k_FigureMeshPath = "Assets/_models/PointCloud/Figure_A.ply";
        const string k_FigureMaterialPath = "Assets/Materials/O0FigureLit.mat";

        /// <summary>
        /// The void. Near black, because the opening image is one light in a dark room -- the
        /// script's 「完全灰色的空間」 is where the beat ENDS up once the two 光圈 are lit, not where
        /// it starts. Not pure black: at exactly zero the fog has no colour to carry and the
        /// floor stops having a distance, which flattens the pool into a decal.
        /// </summary>
        static readonly Color k_Void = new(0.012f, 0.013f, 0.017f);

        static readonly Color k_Purple = new(0.62f, 0.22f, 1.00f);
        static readonly Color k_Yellow = new(1.00f, 0.78f, 0.12f);

        /// <summary>
        /// The white fixture. High and wide: a 72-degree cone from 4.4 m lands a pool about
        /// 6.4 m across, which is wider than the play space, so both players are inside one
        /// light with room to walk before either is picked out by their own.
        /// </summary>
        const float k_CentreHeight = 4.4f;
        const float k_CentreAngle = 84f;

        /// <summary>Where the two 真人 stand, and where their 光圈 hang.</summary>
        const float k_FigureSpread = 1.05f;
        const float k_FigureDepth = 0.5f;
        const float k_ColourHeight = 3.3f;
        const float k_ColourAngle = 24f;

        /// <summary>Builds everything into <paramref name="contentRoot"/>. False means the harness should fail.</summary>
        public static bool Populate(GameObject contentRoot)
        {
            var ghostShader = Shader.Find("CompositeBody/GhostHalf");
            if (!CheckShader(ghostShader, "CompositeBody/GhostHalf")) return false;


            var spotShader = Shader.Find("CompositeBody/VolumetricSpot");
            if (!CheckShader(spotShader, "CompositeBody/VolumetricSpot")) return false;

            var figureShader = Shader.Find("CompositeBody/PointCloudLit");
            if (!CheckShader(figureShader, "CompositeBody/PointCloudLit")) return false;

            var litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
            {
                Debug.LogError("[O0] Shader 'Universal Render Pipeline/Lit' not found.");
                return false;
            }

            BuildAir(contentRoot);
            BuildFloor(contentRoot, litShader);
            BuildCentre(contentRoot, litShader, spotShader);
            BuildFigures(contentRoot, figureShader, spotShader);
            if (!BuildDust(contentRoot)) return false;
            if (!BuildSound(contentRoot)) return false;
            BuildBlob(contentRoot, ghostShader);

            return true;
        }

        #region The room

        /// <summary>
        /// The air. Height fog, and the thing that makes a beam visible at all.
        ///
        /// Thin, and much thinner than the light test: that scene exists to look at beams, this
        /// one has to let two people see each other across it. The falloff height is low so what
        /// haze there is sits around the figures rather than filling the room, which is also
        /// what keeps the black above the slab black.
        /// </summary>
        static void BuildAir(GameObject parent)
        {
            var go = new GameObject("Air");
            go.transform.SetParent(parent.transform, false);

            var zone = go.AddComponent<CompositeFogZone>();
            var so = new SerializedObject(zone);
            so.FindProperty("m_Color").colorValue = k_Void;
            so.FindProperty("m_Density").floatValue = 0.055f;
            so.FindProperty("m_FalloffHeight").floatValue = 1.9f;
            so.FindProperty("m_GlowStrength").floatValue = 0.35f;
            so.FindProperty("m_GlowRadius").floatValue = 0.8f;
            so.FindProperty("m_MaxOpacity").floatValue = 0.97f;
            so.ApplyModifiedPropertiesWithoutUndo();

            zone.Rebuild();
        }

        /// <summary>
        /// 「全灰平面」. Wide enough that the fog reaches full opacity before the edge does, so the
        /// plane has no visible boundary and the room has no size.
        /// </summary>
        static void BuildFloor(GameObject parent, Shader litShader)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Grey Plane";
            floor.transform.SetParent(parent.transform, false);
            floor.transform.localScale = Vector3.one * 6f;          // a Unity Plane is 10 m at 1

            var mat = LoadOrCreate(k_FloorMaterialPath, litShader, "O0Floor");
            mat.SetColor("_BaseColor", new Color(0.46f, 0.46f, 0.47f));
            // Matte. A smooth floor under a single hard downlight returns a specular hotspot
            // that reads as wet, and the reference image's floor is chalk.
            mat.SetFloat("_Smoothness", 0.04f);
            EditorUtility.SetDirty(mat);
            floor.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>
        /// The white light the players arrive in, and the slab standing in it.
        ///
        /// The slab is EMISSIVE, not merely lit. In the reference it is the brightest thing in
        /// frame, brighter than the floor beneath it -- and a white vertical face under a
        /// downlight is lit at a grazing angle, so it would come out darker than the floor, not
        /// lighter. Only a surface making its own light reads that way round.
        ///
        /// Shadows ON for this fixture alone. The dark wedge the slab throws across its own pool
        /// is most of what makes that image read as a light rather than as a painted circle, and
        /// a box is the one occluder in this beat that casts a clean one -- the 真人 are point
        /// clouds, and a shadow map of several hundred thousand loose points is a cloud of holes.
        /// </summary>
        static void BuildCentre(GameObject parent, Shader litShader, Shader spotShader)
        {
            var root = new GameObject("Centre");
            root.transform.SetParent(parent.transform, false);

            // The slab. Turned a few degrees off square: dead-on it reads as a UI panel, and the
            // reference's is rotated just enough to show that it is an object in a room.
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Slab";
            UnityEngine.Object.DestroyImmediate(slab.GetComponent<Collider>());
            slab.transform.SetParent(root.transform, false);
            slab.transform.localPosition = new Vector3(0.15f, 1.1f, 3.1f);
            slab.transform.localRotation = Quaternion.Euler(0f, 7f, 0f);
            slab.transform.localScale = new Vector3(3.0f, 2.2f, 0.14f);

            var slabMat = LoadOrCreate(k_SlabMaterialPath, litShader, "O0Slab");
            // Near black, not white. The slab is a thing that MAKES light, not a thing lit by
            // it: left white it stayed a flat grey card all the way through the fade, because a
            // white box under the centre lamp is still a white box once its emission is gone.
            slabMat.SetColor("_BaseColor", new Color(0.03f, 0.03f, 0.035f));
            slabMat.SetFloat("_Smoothness", 0.1f);
            slabMat.EnableKeyword("_EMISSION");
            slabMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            // Over 1 on purpose even though the pipeline runs m_SupportsHDR 0: the clamp happens
            // at the end of the frame, and the slab is meant to be the one thing in the beat that
            // is simply at white.
            slabMat.SetColor("_EmissionColor", new Color(1.35f, 1.36f, 1.4f));
            EditorUtility.SetDirty(slabMat);
            slab.GetComponent<MeshRenderer>().sharedMaterial = slabMat;

            // The fixture.
            var lampGO = new GameObject("Lamp");
            lampGO.transform.SetParent(root.transform, false);
            lampGO.transform.localPosition = new Vector3(0f, k_CentreHeight, 0.8f);
            lampGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var lamp = lampGO.AddComponent<Light>();
            lamp.type = LightType.Spot;
            lamp.color = new Color(0.96f, 0.97f, 1f);
            lamp.intensity = 24f;
            lamp.range = k_CentreHeight * 1.6f;     // range is falloff, not throw -- see VolumetricSpot.Pull
            lamp.spotAngle = k_CentreAngle;
            lamp.innerSpotAngle = k_CentreAngle * 0.18f;   // wide penumbra: the pool has no rim
            lamp.shadows = LightShadows.Soft;
            lamp.shadowStrength = 0.92f;

            // The air in it. Thin, because the reference shows a POOL and not a shaft -- there
            // is no visible cone above the slab in that image, and a 72-degree cone at the
            // density the two 光圈 use would fill the room with white.
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beam.name = "Beam";
            UnityEngine.Object.DestroyImmediate(beam.GetComponent<Collider>());
            beam.transform.SetParent(root.transform, false);

            var beamMat = LoadOrCreate(k_SpotWhitePath, spotShader, "O0SpotWhite");
            beamMat.SetFloat("_Intensity", 1.1f);
            beamMat.SetFloat("_EdgeGain", 1.0f);          // softest: no rim at all
            beamMat.SetFloat("_EdgeSoftness", 0.95f);
            beamMat.SetFloat("_DistanceFalloff", 0.4f);
            beamMat.SetFloat("_RangeFade", 0.05f);
            beamMat.SetFloat("_BaseGlow", 0f);
            beamMat.SetFloat("_Anisotropy", 0.55f);
            beamMat.SetFloat("_Steps", 10f);
            beamMat.SetFloat("_DepthSoftness", 0.2f);
            EditorUtility.SetDirty(beamMat);
            beam.GetComponent<MeshRenderer>().sharedMaterial = beamMat;

            var spot = beam.AddComponent<VolumetricSpot>();
            var so = new SerializedObject(spot);
            so.FindProperty("m_MatchLight").objectReferenceValue = lamp;
            so.FindProperty("m_Range").floatValue = k_CentreHeight;
            so.FindProperty("m_ConeAngle").floatValue = k_CentreAngle;
            so.FindProperty("m_Color").colorValue = new Color(0.90f, 0.93f, 1f);
            so.FindProperty("m_Intensity").floatValue = 1.1f;
            so.FindProperty("m_Density").floatValue = 0.22f;
            so.FindProperty("m_Anisotropy").floatValue = 0.55f;
            so.FindProperty("m_Steps").intValue = 10;
            so.FindProperty("m_SceneDepthOcclusion").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            spot.Rebuild();
        }

        /// <summary>
        /// 真人 A and B, and the 光圈 that will claim them.
        ///
        /// The coloured beams are built at reveal 0 -- dark, and their renderers switched off by
        /// VolumetricSpot itself, so they cost nothing until the cue brings them up. The lamps
        /// start at zero intensity for the same reason and are driven with the beam, because a
        /// 光圈 whose haze fades in over a figure already lit in that colour arrives backwards.
        /// </summary>
        static void BuildFigures(GameObject parent, Shader figureShader, Shader spotShader)
        {
            Mesh mesh = LoadMesh(k_FigureMeshPath);
            if (mesh == null)
                Debug.LogWarning($"[O0] no point cloud at {k_FigureMeshPath}; the 光圈 will stand empty.");

            Material figureMat = LoadOrCreate(k_FigureMaterialPath, figureShader, "O0FigureLit");
            figureMat.SetColor("_BaseColor", Color.white);
            figureMat.SetFloat("_ScanColorMix", 0.2f);
            figureMat.SetFloat("_PointSize", 0.0045f);
            figureMat.SetFloat("_Exposure", 0.7f);
            figureMat.SetFloat("_AmbientBoost", 0.4f);
            figureMat.SetFloat("_Wrap", 0.4f);
            EditorUtility.SetDirty(figureMat);

            // One material between both 光圈: everything that differs between them -- colour,
            // position, reveal -- goes through the property block, and the two things that
            // cannot (the depth keyword and ZTest) are the same for both.
            Material beamMat = LoadOrCreate(k_SpotColourPath, spotShader, "O0SpotColour");
            beamMat.SetFloat("_Intensity", 1.35f);
            beamMat.SetFloat("_EdgeGain", 1.6f);
            beamMat.SetFloat("_EdgeSoftness", 0.85f);
            beamMat.SetFloat("_DistanceFalloff", 0.6f);
            beamMat.SetFloat("_RangeFade", 0.05f);
            beamMat.SetFloat("_BaseGlow", 0f);
            beamMat.SetFloat("_Anisotropy", 0.65f);
            beamMat.SetFloat("_Steps", 12f);
            beamMat.SetFloat("_DepthSoftness", 0.15f);
            EditorUtility.SetDirty(beamMat);

            BuildFigure(parent, "Human A", new Vector3(-k_FigureSpread, 0f, k_FigureDepth),
                        k_Purple, 22f, mesh, figureMat, beamMat);
            BuildFigure(parent, "Human B", new Vector3(k_FigureSpread, 0f, k_FigureDepth),
                        k_Yellow, -22f, mesh, figureMat, beamMat);
        }

        static void BuildFigure(GameObject parent, string name, Vector3 position, Color colour,
                                float yaw, Mesh mesh, Material figureMat, Material beamMat)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent.transform, false);
            root.transform.localPosition = position;
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            if (mesh != null)
            {
                var figure = new GameObject("Figure");
                figure.transform.SetParent(root.transform, false);
                figure.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = figure.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = figureMat;
                renderer.localBounds = Grow(mesh.bounds, 2.5f);
                // Nothing to receive from, and casting is what makes a point cloud's shadow a
                // cloud of holes. Off on both counts, and it takes the figures out of the
                // centre fixture's shadow map entirely.
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                figure.AddComponent<PointCloudFigure>();
            }

            var lampGO = new GameObject("Lamp");
            lampGO.transform.SetParent(root.transform, false);
            lampGO.transform.localPosition = new Vector3(0f, k_ColourHeight, 0f);
            lampGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var lamp = lampGO.AddComponent<Light>();
            lamp.type = LightType.Spot;
            lamp.color = colour;
            lamp.intensity = 0f;                 // the cue brings this up with the haze
            lamp.range = 5f;
            lamp.spotAngle = k_ColourAngle;
            lamp.innerSpotAngle = k_ColourAngle * 0.45f;
            lamp.shadows = LightShadows.None;

            var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beam.name = "Beam";
            UnityEngine.Object.DestroyImmediate(beam.GetComponent<Collider>());
            beam.transform.SetParent(root.transform, false);
            beam.GetComponent<MeshRenderer>().sharedMaterial = beamMat;

            var spot = beam.AddComponent<VolumetricSpot>();
            var so = new SerializedObject(spot);
            so.FindProperty("m_MatchLight").objectReferenceValue = lamp;
            so.FindProperty("m_Range").floatValue = k_ColourHeight;
            so.FindProperty("m_ConeAngle").floatValue = k_ColourAngle;
            so.FindProperty("m_UseRoleColor").boolValue = false;
            so.FindProperty("m_Color").colorValue = colour;
            so.FindProperty("m_Intensity").floatValue = 1.35f;
            so.FindProperty("m_Density").floatValue = 0.95f;
            so.FindProperty("m_Anisotropy").floatValue = 0.65f;
            so.FindProperty("m_Steps").intValue = 12;
            so.FindProperty("m_SceneDepthOcclusion").boolValue = true;
            so.FindProperty("m_Reveal").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();
            spot.Rebuild();
        }

        /// <summary>
        /// Hand the beat the objects its cue drives.
        ///
        /// Explicit serialized references rather than a GetComponentsInChildren at runtime, so
        /// the inspector shows what the cue touches and a renamed object fails here, in a build
        /// step with a log line, instead of silently leaving one 光圈 dark in a show.
        /// </summary>
        public static bool Wire(O0ArrivalBeat beat, GameObject contentRoot)
        {
            var so = new SerializedObject(beat);

            VolumetricSpot centreBeam = Find<VolumetricSpot>(contentRoot, "Centre/Beam");
            Light centreLamp = Find<Light>(contentRoot, "Centre/Lamp");
            Renderer slab = Find<Renderer>(contentRoot, "Centre/Slab");
            if (centreBeam == null || centreLamp == null || slab == null) return false;

            so.FindProperty("m_CentreBeam").objectReferenceValue = centreBeam;
            so.FindProperty("m_CentreLamp").objectReferenceValue = centreLamp;
            so.FindProperty("m_Slab").objectReferenceValue = slab;
            so.FindProperty("m_CentreLampIntensity").floatValue = centreLamp.intensity;
            so.FindProperty("m_SlabEmission").colorValue =
                slab.sharedMaterial != null && slab.sharedMaterial.HasProperty("_EmissionColor")
                    ? slab.sharedMaterial.GetColor("_EmissionColor")
                    : Color.white;
            so.FindProperty("m_VoidColor").colorValue = k_Void;

            string[] figures = { "Human A", "Human B" };
            SerializedProperty beams = so.FindProperty("m_ColourBeams");
            SerializedProperty lamps = so.FindProperty("m_ColourLamps");
            beams.arraySize = figures.Length;
            lamps.arraySize = figures.Length;

            for (int i = 0; i < figures.Length; i++)
            {
                var beam = Find<VolumetricSpot>(contentRoot, $"{figures[i]}/Beam");
                var lamp = Find<Light>(contentRoot, $"{figures[i]}/Lamp");
                if (beam == null || lamp == null) return false;
                beams.GetArrayElementAtIndex(i).objectReferenceValue = beam;
                lamps.GetArrayElementAtIndex(i).objectReferenceValue = lamp;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(beat);

            Debug.Log($"[O0] Cue wired: 1 white fixture + slab, {figures.Length} 光圈.");
            return true;
        }

        static T Find<T>(GameObject root, string path) where T : Component
        {
            Transform t = root.transform.Find(path);
            if (t == null)
            {
                Debug.LogError($"[O0] RESULT: FAIL - no '{path}' under {root.name}.");
                return null;
            }
            var component = t.GetComponent<T>();
            if (component == null)
                Debug.LogError($"[O0] RESULT: FAIL - '{path}' has no {typeof(T).Name}.");
            return component;
        }

        static Bounds Grow(Bounds b, float m) { b.Expand(m); return b; }

        static Mesh LoadMesh(string path)
        {
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (o is Mesh m) return m;
            }
            return null;
        }

        #endregion

        static bool CheckShader(Shader shader, string name)
        {
            if (shader == null)
            {
                Debug.LogError($"[O0] Shader '{name}' not found.");
                return false;
            }
            if (ShaderUtil.ShaderHasError(shader))
            {
                foreach (var m in ShaderUtil.GetShaderMessages(shader))
                    Debug.LogError($"[O0] {name} {m.severity} line {m.line}: {m.message}");
                Debug.LogError($"[O0] Shader '{name}' has compile errors.");
                return false;
            }
            Debug.Log($"[O0] Shader '{name}' compiled clean.");
            return true;
        }

        #region Dust

        /// <summary>
        /// 「遠處漂浮著少量灰塵與微弱粒子」, as a VFX Graph rather than the Shuriken system it replaces.
        ///
        /// GPU simulation is the whole reason: a couple of hundred motes with a twenty-second
        /// life, drifting under drag, cost nothing on the GPU and cost a per-particle CPU update
        /// on Shuriken -- in a beat where the CPU is already running two headsets' worth of
        /// netcode.
        ///
        /// VFX Graph simulates from a per-instance seed, so the two headsets will NOT see the
        /// same motes. That is the objection that ruled it out for the point clouds, where two
        /// people reaching for the same body have to be reaching for the same points, and it
        /// does not apply here: nobody can correlate a speck of dust across a room, and the
        /// script asks for 遠處 -- distant, and by implication not something either player is
        /// looking straight at.
        ///
        /// The asset is Assets/VFX/O0Dust.vfx, started from the package's own Simple_Loop
        /// template and retuned. It opens and edits in the VFX Graph window like any other.
        /// </summary>
        static bool BuildDust(GameObject parent)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(k_DustAssetPath);
            if (asset == null)
            {
                Debug.LogError($"[O0] RESULT: FAIL - no VFX asset at {k_DustAssetPath}.");
                return false;
            }

            var go = new GameObject("Dust");
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = Vector3.zero;

            var vfx = go.AddComponent<UnityEngine.VFX.VisualEffect>();
            vfx.visualEffectAsset = asset;
            // Fixed rather than random, so a rebuilt scene and a rerun render the same dust.
            // The two headsets still differ -- see above -- but a build that differs from
            // itself makes every screenshot comparison worthless.
            vfx.startSeed = 20261109;
            vfx.resetSeedOnPlay = false;

            Debug.Log($"[O0] Dust: {asset.name} (VFX Graph).");
            return true;
        }

        #endregion

        #region Sound

        static bool BuildSound(GameObject parent)
        {
            var bed = LoadClip("O0_AmbienceLow_loop.wav");
            var water = LoadClip("Distant_Water.wav");
            var furniture = LoadClip("Distant_Furniture.wav");
            var voice = LoadClip("Distant_Voice.wav");

            if (bed == null || water == null || furniture == null || voice == null)
            {
                Debug.LogError($"[O0] Placeholder audio missing from {k_AudioDir}. " +
                               "Run 'py Tools/generate_placeholder_audio.py' from the repo root first.");
                return false;
            }

            var go = new GameObject("DistantSoundBed");
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = new Vector3(0f, 1.5f, 0f);

            var bedSource = go.AddComponent<AudioSource>();
            bedSource.clip = bed;
            bedSource.loop = true;
            bedSource.playOnAwake = false;
            bedSource.volume = 0.55f;
            // Mostly non-directional: the low bed is the room itself, not a thing in it.
            bedSource.spatialBlend = 0.15f;

            var sources = new AudioSource[3];
            for (int i = 0; i < sources.Length; i++)
            {
                var child = new GameObject($"DistantSource_{i}");
                child.transform.SetParent(go.transform, false);

                var source = child.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.volume = 0.42f;
                source.spatialBlend = 1f;              // fully positional, so direction reads
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 2f;
                source.maxDistance = 26f;

                // What makes it "muffled, cannot be made out" rather than merely quiet.
                var lowpass = child.AddComponent<AudioLowPassFilter>();
                lowpass.cutoffFrequency = 820f;
                lowpass.lowpassResonanceQ = 1.1f;

                sources[i] = source;
            }

            var soundBed = go.AddComponent<DistantSoundBed>();
            var so = new SerializedObject(soundBed);
            so.FindProperty("m_Bed").objectReferenceValue = bedSource;

            var clips = so.FindProperty("m_Clips");
            clips.arraySize = 3;
            clips.GetArrayElementAtIndex(0).objectReferenceValue = water;
            clips.GetArrayElementAtIndex(1).objectReferenceValue = furniture;
            clips.GetArrayElementAtIndex(2).objectReferenceValue = voice;

            var sourceProp = so.FindProperty("m_Sources");
            sourceProp.arraySize = sources.Length;
            for (int i = 0; i < sources.Length; i++)
                sourceProp.GetArrayElementAtIndex(i).objectReferenceValue = sources[i];

            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("[O0] Sound bed wired with 3 placeholder cues.");
            return true;
        }

        static AudioClip LoadClip(string fileName) =>
            AssetDatabase.LoadAssetAtPath<AudioClip>($"{k_AudioDir}/{fileName}");

        #endregion

        #region Blob

        static void BuildBlob(GameObject parent, Shader ghostShader)
        {
            var go = new GameObject("OtherPlayerBlob");
            go.transform.SetParent(parent.transform, false);

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Blob";
            sphere.transform.SetParent(go.transform, false);
            Object.DestroyImmediate(sphere.GetComponent<Collider>());

            var mat = LoadOrCreate(k_BlobMaterialPath, ghostShader, "RoleBlob");
            // Tinted per role at runtime; this is only the neutral authored state. Soft fill and
            // a broad rim, so it reads as a presence with an edge rather than as a glass ball.
            mat.SetColor("_GhostColor", new Color(0.55f, 0.60f, 0.62f));
            mat.SetFloat("_FillAlpha", 0.16f);
            mat.SetFloat("_RimPower", 1.9f);
            mat.SetFloat("_RimIntensity", 2.1f);
            mat.SetFloat("_EdgeAlpha", 0.55f);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);

            var renderer = sphere.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var presenter = go.AddComponent<RoleBlobPresenter>();
            var so = new SerializedObject(presenter);
            so.FindProperty("m_BlobRenderer").objectReferenceValue = renderer;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("[O0] Other-player blob wired.");
        }

        #endregion

        static Material LoadOrCreate(string path, Shader shader, string name)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            return mat;
        }
    }
}
