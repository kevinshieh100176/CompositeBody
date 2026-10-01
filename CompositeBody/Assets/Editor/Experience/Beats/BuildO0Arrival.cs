using System.IO;
using UnityEditor;
using UnityEngine;
using CompositeBody.Experience;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Authors the contents of O-0 進入 into the beat's content root: the sea surface, the
    /// drifting 「框」, the sparse motes, the distant sound bed and the other player's blob.
    ///
    /// All five of the beat's layers are built here together rather than one system at a time.
    /// The point of a vertical slice is that the beat is finishable: it is better to have O-0
    /// complete and crude than to have the water, the frames and the audio each half-built
    /// across every beat at once.
    /// </summary>
    public static class BuildO0Arrival
    {
        const string k_SeaMaterialPath = "Assets/Materials/SeaSurface.mat";
        const string k_BlobMaterialPath = "Assets/Materials/RoleBlob.mat";
        const string k_MoteMaterialPath = "Assets/Materials/VoidMote.mat";
        const string k_SeaMeshPath = "Assets/Meshes/SeaGrid.mesh";
        const string k_AudioDir = "Assets/Audio/Placeholder";

        /// <summary>Sea extent in metres, and how many quads across. ~1m spacing carries the swell.</summary>
        const float k_SeaSize = 64f;
        const int k_SeaDivisions = 64;

        /// <summary>
        /// Authored positions for the frames. Fixed rather than random so both the scene and the
        /// deterministic drift phase derived from these positions are reproducible across runs.
        /// </summary>
        static readonly (Vector3 pos, Vector3 euler, float w, float h)[] k_Frames =
        {
            (new Vector3(-3.4f, 1.15f, 5.2f), new Vector3(0f, 18f, -4f), 1.05f, 2.15f),   // a door
            (new Vector3(4.1f, 1.60f, 6.8f), new Vector3(0f, -26f, 3f), 1.35f, 1.10f),    // a window
            (new Vector3(-6.2f, 0.75f, 9.4f), new Vector3(0f, 42f, 6f), 0.95f, 1.95f),
            (new Vector3(7.8f, 2.35f, 11.0f), new Vector3(0f, -12f, -7f), 1.55f, 1.25f),
            (new Vector3(0.9f, 0.45f, 12.6f), new Vector3(0f, 6f, 2f), 1.15f, 2.30f),
            (new Vector3(-9.5f, 1.95f, 14.2f), new Vector3(0f, 55f, -3f), 1.25f, 1.15f),
        };

        /// <summary>Builds everything into <paramref name="contentRoot"/>. False means the harness should fail.</summary>
        public static bool Populate(GameObject contentRoot)
        {
            var seaShader = Shader.Find("CompositeBody/SeaSurface");
            if (!CheckShader(seaShader, "CompositeBody/SeaSurface")) return false;

            var ghostShader = Shader.Find("CompositeBody/GhostHalf");
            if (!CheckShader(ghostShader, "CompositeBody/GhostHalf")) return false;

            var moteShader = Shader.Find("CompositeBody/TraceParticle");
            if (!CheckShader(moteShader, "CompositeBody/TraceParticle")) return false;

            var litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
            {
                Debug.LogError("[O0] Shader 'Universal Render Pipeline/Lit' not found.");
                return false;
            }

            BuildSea(contentRoot, seaShader);
            BuildFrames(contentRoot, litShader);
            BuildMotes(contentRoot, moteShader);
            if (!BuildSound(contentRoot)) return false;
            BuildBlob(contentRoot, ghostShader);

            return true;
        }

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

        #region Sea

        static void BuildSea(GameObject parent, Shader seaShader)
        {
            var go = new GameObject("SeaSurface");
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = Vector3.zero;

            var mesh = LoadOrCreateSeaMesh();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = LoadOrCreateSeaMaterial(seaShader);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Debug.Log($"[O0] Sea: {mesh.vertexCount} verts over {k_SeaSize}m.");
        }

        /// <summary>
        /// A subdivided grid, because the shader displaces vertices. Unity's built-in Plane is
        /// 10 quads across, which at this extent puts a vertex every 6m -- the waves would have
        /// nothing to move.
        /// </summary>
        static Mesh LoadOrCreateSeaMesh()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(k_SeaMeshPath);
            if (existing != null && existing.vertexCount == (k_SeaDivisions + 1) * (k_SeaDivisions + 1))
                return existing;

            int side = k_SeaDivisions + 1;
            var verts = new Vector3[side * side];
            var normals = new Vector3[side * side];
            var uvs = new Vector2[side * side];
            var tris = new int[k_SeaDivisions * k_SeaDivisions * 6];

            float step = k_SeaSize / k_SeaDivisions;
            float half = k_SeaSize * 0.5f;

            for (int z = 0; z < side; z++)
            {
                for (int x = 0; x < side; x++)
                {
                    int i = z * side + x;
                    verts[i] = new Vector3(x * step - half, 0f, z * step - half);
                    normals[i] = Vector3.up;
                    uvs[i] = new Vector2((float)x / k_SeaDivisions, (float)z / k_SeaDivisions);
                }
            }

            int t = 0;
            for (int z = 0; z < k_SeaDivisions; z++)
            {
                for (int x = 0; x < k_SeaDivisions; x++)
                {
                    int bl = z * side + x;
                    int br = bl + 1;
                    int tl = bl + side;
                    int tr = tl + 1;

                    tris[t++] = bl; tris[t++] = tl; tris[t++] = br;
                    tris[t++] = br; tris[t++] = tl; tris[t++] = tr;
                }
            }

            var mesh = new Mesh { name = "SeaGrid" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();

            // Generous bounds: the shader moves vertices the CPU never sees, and a mesh culled
            // on its flat bounds pops out of view at grazing angles, which is the only angle
            // this surface is ever looked at.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(k_SeaSize, 4f, k_SeaSize));

            Directory.CreateDirectory("Assets/Meshes");
            if (existing != null) AssetDatabase.DeleteAsset(k_SeaMeshPath);
            AssetDatabase.CreateAsset(mesh, k_SeaMeshPath);
            AssetDatabase.SaveAssets();
            return mesh;
        }

        static Material LoadOrCreateSeaMaterial(Shader shader)
        {
            var mat = LoadOrCreate(k_SeaMaterialPath, shader, "SeaSurface");

            // Near black looked down at, picking up a cold sheen towards the horizon: the floor
            // has to read as water without any light in the scene being about water.
            mat.SetColor("_DeepColor", new Color(0.014f, 0.026f, 0.040f));
            mat.SetColor("_SheenColor", new Color(0.26f, 0.38f, 0.48f));
            mat.SetFloat("_Sheen", 0.9f);
            mat.SetFloat("_WaveAmp", 0.055f);
            mat.SetFloat("_WaveScale", 0.42f);
            mat.SetFloat("_WaveSpeed", 0.40f);
            mat.SetFloat("_FresnelPower", 3.4f);
            mat.SetColor("_SpecColor2", new Color(0.50f, 0.62f, 0.74f));
            mat.SetFloat("_SpecSharpness", 52f);
            mat.SetFloat("_SpecStrength", 0.45f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        #endregion

        #region Frames

        static void BuildFrames(GameObject parent, Shader litShader)
        {
            var root = new GameObject("Frames");
            root.transform.SetParent(parent.transform, false);

            var mat = new Material(litShader) { name = "FrameTimber" };
            mat.SetColor("_BaseColor", new Color(0.085f, 0.080f, 0.078f));
            mat.SetFloat("_Smoothness", 0.22f);

            for (int i = 0; i < k_Frames.Length; i++)
            {
                var (pos, euler, w, h) = k_Frames[i];

                var frame = new GameObject($"Frame_{i:00}");
                frame.transform.SetParent(root.transform, false);
                frame.transform.localPosition = pos;
                frame.transform.localRotation = Quaternion.Euler(euler);

                BuildFrameBars(frame, w, h, 0.055f, mat);

                var drift = frame.AddComponent<FrameDrift>();
                drift.Capture();
            }

            Debug.Log($"[O0] Frames: {k_Frames.Length}.");
        }

        /// <summary>Four bars round an empty middle. The hole is the point -- it is a frame, not a panel.</summary>
        static void BuildFrameBars(GameObject parent, float width, float height, float thickness, Material mat)
        {
            AddBar(parent, "Top", new Vector3(0f, height * 0.5f, 0f), new Vector3(width + thickness, thickness, thickness), mat);
            AddBar(parent, "Bottom", new Vector3(0f, -height * 0.5f, 0f), new Vector3(width + thickness, thickness, thickness), mat);
            AddBar(parent, "Left", new Vector3(-width * 0.5f, 0f, 0f), new Vector3(thickness, height, thickness), mat);
            AddBar(parent, "Right", new Vector3(width * 0.5f, 0f, 0f), new Vector3(thickness, height, thickness), mat);
        }

        static void AddBar(GameObject parent, string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = name;
            bar.transform.SetParent(parent.transform, false);
            bar.transform.localPosition = localPos;
            bar.transform.localScale = scale;
            bar.GetComponent<MeshRenderer>().sharedMaterial = mat;

            // Nothing is grabbable in O-0; the only verbs the player has yet are looking and moving.
            Object.DestroyImmediate(bar.GetComponent<Collider>());
        }

        #endregion

        #region Motes

        static void BuildMotes(GameObject parent, Shader moteShader)
        {
            var go = new GameObject("VoidMotes");
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = new Vector3(0f, 1.2f, 4f);

            var system = go.AddComponent<ParticleSystem>();
            var renderer = go.GetComponent<ParticleSystemRenderer>();

            var mat = LoadOrCreate(k_MoteMaterialPath, moteShader, "VoidMote");
            mat.SetColor("_Color", new Color(0.62f, 0.72f, 0.80f));
            mat.SetFloat("_Intensity", 1.1f);
            mat.SetFloat("_Falloff", 2.6f);
            EditorUtility.SetDirty(mat);

            renderer.sharedMaterial = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.None;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // "遠處漂浮著少量灰塵與微弱粒子" -- sparse and barely there. Dense dust would make the
            // void feel like weather instead of like a space that has not finished existing.
            var main = system.main;
            main.loop = true;
            main.duration = 12f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(14f, 26f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.012f, 0.055f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.045f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.55f, 0.64f, 0.72f, 0.5f), new Color(0.78f, 0.82f, 0.88f, 0.85f));
            main.gravityModifier = 0f;
            main.maxParticles = 420;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = true;

            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 22f;

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(34f, 5.5f, 34f);

            var noise = system.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.09f);
            noise.frequency = 0.14f;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.05f);
            noise.damping = true;

            var colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f),
                    new GradientAlphaKey(1f, 0.70f), new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            Debug.Log("[O0] Motes configured.");
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
