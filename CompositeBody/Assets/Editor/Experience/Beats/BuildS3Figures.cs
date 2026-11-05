using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using CompositeBody.Experience;
using CompositeBody.Multiplayer;
using CompositeBody.PointClouds;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// S3's four 真人 and the three objects they bring with them, cued on and off across S3-2,
    /// S3-3 and S3-4.
    ///
    /// 「在s3-2的時候dissolve in figure a4 還有他game object裡面的cardboard 跟t shirt，在s3-3的時候
    /// dissolve in figure b4 還有他game object裡面的cardboard 跟stone，在s3-4的時候兩組都要dissolve
    /// out，然後同時在s3-4 玩家a 會看到Figure_B5 dissolve in，玩家b會看到Figure_A5 dissolve in」
    ///
    /// So three things happen here, and only the last of them is asymmetric:
    ///
    /// <list type="bullet">
    /// <item><b>S3-2</b> brings in Figure_A4 with its Cardboard and its T-shirt.</item>
    /// <item><b>S3-3</b> brings in Figure_B4 with its Cardboard (1) and its Stone.</item>
    /// <item><b>S3-4</b> takes both groups away again, and in the same breath each player is
    /// shown the OTHER one's 真人: 玩家A gets Figure_B5, 玩家B gets Figure_A5. Neither ever sees
    /// the one that belongs to them.</item>
    /// <item><b>The end of S3-6</b> takes those last two away over ten seconds and fades the
    /// screen to white over the last five of them, so the white rises while they are still
    /// coming apart rather than over an empty room.</item>
    /// </list>
    ///
    /// THE TRACKS GO ON MAIN, NOT IN S3_2/S3_3/S3_4.playable, and that is not a style choice.
    /// <see cref="FigurePresenceTrack"/> and <see cref="PropPresenceTrack"/> work through a
    /// track MIXER, which answers for the whole length of the timeline it is on -- that is the
    /// entire reason they exist rather than being plain clips, and it is what makes a figure
    /// ABSENT from the first frame of the show instead of standing in the room until its cue.
    /// A mixer inside a beat's sub-timeline only runs while that beat's control clip is playing,
    /// so the same tracks authored in S3_2.playable would leave Figure_A4, its carton and its
    /// t-shirt in the room for the twenty minutes of O, S1 and S2 that come first. On Main the
    /// mixers cover all 38 minutes, which is why the show's other six figure cues are there too,
    /// under beat-named tracks exactly like these.
    ///
    /// TWO MECHANISMS, BECAUSE TWO KINDS OF MATERIAL -- and the line between them moved on
    /// 2026-11-05. 「幫我把這兩個物件換成點雲版本」: the t-shirt and the stone are point clouds now,
    /// baked out of their own meshes by BakeScenePointCloud, so the only things
    /// left on the mesh path are the two cartons. A 真人 and the thing it is carrying therefore
    /// come apart through the same shader, which is the whole point of the change: a scanned
    /// body breaking into points while the shirt beside it wipes out through an alpha clip read
    /// as two different events happening at once.
    ///
    /// So: the 真人, the t-shirt and the stone dissolve through <see cref="PointCloudFigure"/>'s
    /// _Dissolve on a <see cref="FigurePresenceTrack"/>; the two cartons dissolve through
    /// <see cref="PropPresence"/>'s alpha clip on a <see cref="PropPresenceTrack"/>. Both kinds
    /// are driven from a curve on a timeline against the same two cues, so they still read as
    /// one event.
    ///
    /// AND THE CARTONS NEED A MATERIAL OF THEIR OWN. Their source material is opaque with alpha
    /// clipping off, so neither PropPresence mode would have moved them at all -- and Cardbox_MAT
    /// is shared, so it cannot simply be switched over. This builds a copy per source material
    /// and hands it to <see cref="BuildO1Timeline.MakePropClippable"/>, the same treatment, the
    /// same noise and the same clip setup O-1's cartons and lamp already get.
    ///
    /// Unity.exe -batchmode -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildS3Figures.Run
    /// </summary>
    public static class BuildS3Figures
    {
        const string k_MainScene = "Assets/_Scenes/MainScene.unity";
        const string k_TimelineAsset = "Assets/_Timeline/Main.playable";

        const string k_Content = "Experience/Beats/Beat_S3/Content";

        /// <summary>Seconds for a figure or a prop to gather, and to come apart again.</summary>
        const float k_AppearSeconds = 8f;
        const float k_LeaveSeconds = 8f;

        /// <summary>
        /// How long after its 真人 a carried object starts to gather.
        ///
        /// 「物件晚 2 秒」. The body and the things it brought were cued on the same frame with the
        /// same curve, so the group arrived as one shape and the carton read as part of the
        /// figure rather than as something it was carrying. Holding the objects back makes the
        /// 真人 the thing that arrives and the carton, the t-shirt and the stone the things it
        /// arrives WITH.
        ///
        /// ONLY THE ARRIVAL. The departure at S3-4 is deliberately NOT delayed: that beat is six
        /// seconds long and the dissolve out is already cut to fit it exactly -- see 'handover'
        /// -- so two seconds added there would still be fading two seconds into S3-5, which is
        /// the thing reading the handover off the clip exists to prevent. Both groups therefore
        /// arrive staggered and leave together.
        /// </summary>
        const float k_PropDelaySeconds = 2f;

        /// <summary>
        /// How S3 ends. 「在S3-6結束前最後10秒 dissolve out 然後最後5秒screen fade to white」: the two
        /// 真人 come apart across the last ten seconds of S3-6 and the screen goes white across
        /// the last five, so the white is rising over the back half of the departure rather than
        /// after it. Both are measured BACK FROM the end of the last S3 beat, so a re-cut S3-6
        /// drags them with it.
        /// </summary>
        const float k_FinalOutSeconds = 10f;
        const float k_WhiteSeconds = 5f;

        /// <summary>
        /// The show's one fade track, and the name of the clip this builder owns on it.
        ///
        /// SHARED WITH THE THREE 黑場 TRANSITIONS, which is why this builder deletes its own clip
        /// by name instead of clearing the track the way it clears the ones it made. Emptying
        /// 'Fade' would take out the Onboarding→S1, S1→S2 and S2→S3 blackouts, and those carry
        /// the LightingDirector look switches -- the show would come back with every section
        /// wearing the first one's lighting.
        /// </summary>
        const string k_FadeTrack = "Fade";
        const string k_WhiteClip = "S3-6 轉白";

        /// <summary>
        /// Far enough past the end of any S3 beat to mean "never" in a role column. The role
        /// clip returns "fully gone" for every time at or before its appearsAt, so a column
        /// whose cue is out here is a figure that player simply never sees.
        /// </summary>
        const float k_Never = 100000f;

        /// <summary>A prop: where it lives, the material it came with, and the clipped copy.</summary>
        readonly struct Prop
        {
            public readonly string path;
            public readonly string sourceMaterial;
            public readonly string materialPath;
            public readonly string trackName;

            public Prop(string path, string sourceMaterial, string materialPath, string trackName)
            {
                this.path = path;
                this.sourceMaterial = sourceMaterial;
                this.materialPath = materialPath;
                this.trackName = trackName;
            }
        }

        /// <summary>
        /// THE SOURCE MATERIAL IS NAMED, NOT READ OFF THE RENDERER, and that is the whole reason
        /// this field exists. Taking it from renderer.sharedMaterial works exactly once: on the
        /// second run the renderer is already wearing the clipped copy, so the copy gets copied
        /// onto itself, its _BaseMap is already the noise map, and MakePropClippable dutifully
        /// writes a dissolve map OF a dissolve map -- Cardboard_MAT_Base_color_Dissolve_Dissolve
        /// .png, and another generation for every run after that. Naming the model's own
        /// material makes a re-run land exactly where the first run did.
        /// </summary>
        static readonly Prop[] k_A4Props =
        {
            new($"{k_Content}/Figure_A4/Cardboard",
                "Assets/_models/3d-cardboard-lowpoly/source/Cardbox_MAT.mat",
                "Assets/Materials/S3Cardboard.mat", "S3-2 A4 紙箱"),
        };

        static readonly Prop[] k_B4Props =
        {
            new($"{k_Content}/Figure_B4/Cardboard (1)",
                "Assets/_models/3d-cardboard-lowpoly/source/Cardbox_MAT.mat",
                "Assets/Materials/S3Cardboard.mat", "S3-3 B4 紙箱"),
        };

        /// <summary>
        /// A prop that is made of points: where it lives, the bake it wears, and the mesh that
        /// bake was taken from.
        /// </summary>
        readonly struct CloudProp
        {
            public readonly string path;
            public readonly string cloud;
            public readonly string material;

            /// <summary>
            /// The model's own material, put back on the mesh this cloud replaces.
            ///
            /// That mesh is switched off rather than deleted, and while it was a prop this
            /// builder had swapped it onto a clipped copy whose _BaseMap is a generated dissolve
            /// map. Nothing drives that copy now, so a renderer left wearing it is a t-shirt
            /// that comes back wrong the day somebody switches the geometry on to re-bake it.
            /// </summary>
            public readonly string sourceMaterial;

            public readonly string trackName;

            public CloudProp(string path, string cloud, string material, string sourceMaterial,
                             string trackName)
            {
                this.path = path;
                this.cloud = cloud;
                this.material = material;
                this.sourceMaterial = sourceMaterial;
                this.trackName = trackName;
            }
        }

        /// <summary>
        /// THE BAKES ARE ASSETS, NOT SOMETHING THIS BUILDER MAKES. BakeScenePointCloud walks the
        /// prop's triangles and writes a .ply, and it needs Read/Write on the source FBX to do
        /// it -- which is off again now, because the show does not need it and it doubles both
        /// models in memory. Re-baking either cloud therefore takes three things, in this order:
        /// switch Read/Write back on for the model, switch the hidden source renderer back on
        /// (the bake skips anything disabled, so it would otherwise report "no readable mesh"),
        /// and delete the _Points.mat first if the point size is what is being fixed -- the bake
        /// reuses an existing material and will not repair one.
        ///
        /// The spacing is in the PROP'S OWN LOCAL SPACE, which is why the two numbers differ:
        /// the t-shirt sits at 0.8 and the stone at 3, and both were asked for about 3 mm in the
        /// room -- the density the two 真人 beside them were captured at.
        ///
        ///   T-shirt  -spacing 0.00375  ->  61,687 points
        ///   Stone    -spacing 0.001    ->   7,310 points
        /// </summary>
        static readonly CloudProp[] k_A4Clouds =
        {
            new($"{k_Content}/Figure_A4/T-shirt",
                "Assets/_models/Pointcloud/S3/T-shirt.ply",
                "Assets/_models/Pointcloud/S3/T-shirt_Points.mat",
                "Assets/AtmosphericHouse/Materials/Clothes_hanging_clean.mat",
                "S3-2 A4 T恤"),
        };

        static readonly CloudProp[] k_B4Clouds =
        {
            new($"{k_Content}/Figure_B4/Stone",
                "Assets/_models/Pointcloud/S3/Stone.ply",
                "Assets/_models/Pointcloud/S3/Stone_Points.mat",
                "Assets/_models/SmallProps/Rock.mat",
                "S3-3 B4 石頭"),
        };

        /// <summary>
        /// What a prop cloud looks like standing still and coming apart, IN ROOM METRES.
        ///
        /// Everything the point shader does happens in the cloud's own object space, so an
        /// authored 6 mm of shimmer is 4.8 mm on the t-shirt and 18 mm on the stone unless it is
        /// divided by the object's scale -- which is what <see cref="TuneCloudProp"/> does with
        /// these. They are a step down from the 真人's own 12 mm and 0.6 m: a shirt and a stone
        /// are small things close to the eye, and the figure's amplitude on a 18 cm stone reads
        /// as the stone boiling.
        /// </summary>
        const float k_CloudTurbulence = 0.006f;
        const float k_CloudDrift = 0.3f;
        const float k_CloudSpread = 0.04f;

        /// <summary>Shimmer lobes per metre of room, the same grain the 真人 carry.</summary>
        const float k_CloudGrain = 6f;

        [MenuItem("Tools/S-3/Build S3 Figures and Props")]
        public static void Run()
        {
            Debug.Log("[S3Figures] Starting...");

            // THIS BUILDER DOES NOT OPEN THE SCENE, for the reason BuildS3Orbits gives: every
            // other tool here opens MainScene Single and would throw away whatever is in front
            // of whoever ran it, and this one saves at the end, so it must not be allowed to
            // commit someone else's half-finished work either.
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != k_MainScene)
            {
                Debug.LogError($"[S3Figures] RESULT: FAIL - {k_MainScene} is not the open scene " +
                               $"('{scene.path}' is). Open it and run this again.");
                return;
            }

            if (scene.isDirty)
            {
                Debug.LogError("[S3Figures] RESULT: FAIL - MainScene has unsaved changes and " +
                               "this builder saves the scene. Save or revert first.");
                return;
            }

            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_TimelineAsset);
            if (timeline == null)
            {
                Debug.LogError($"[S3Figures] RESULT: FAIL - no timeline at {k_TimelineAsset}.");
                return;
            }

            PlayableDirector director = FindShowDirector(timeline);
            if (director == null)
            {
                Debug.LogError("[S3Figures] RESULT: FAIL - nothing in MainScene plays " +
                               $"{k_TimelineAsset}, so there is no director to bind a track on.");
                return;
            }

            // THE BEAT TIMES COME OFF THE RUNNING ORDER, never a table. The S3 beats have been
            // re-cut twice already and 「在s3-2的時候」 means whenever S3-2 is, not 1201.
            if (!TryBeat(timeline, "S3-2", out double s32, out _)) return;
            if (!TryBeat(timeline, "S3-3", out double s33, out _)) return;
            if (!TryBeat(timeline, "S3-4", out double s34, out double s34End)) return;
            double s3End = LastS3End(timeline);

            // S3-4 IS SIX SECONDS LONG, so the handover is cut to fit it rather than run at the
            // eight seconds the arrivals use. 「在s3-4的時候兩組都要dissolve out，然後同時...」 is one
            // instant in the running order: a departure that took eight seconds would still be
            // fading two seconds into S3-5, and the two 真人 would still be gathering there too,
            // which is a different beat from the one being asked for. Read off the clip rather
            // than typed in, so a re-cut S3-4 re-times this with it.
            double handover = System.Math.Min(k_AppearSeconds, s34End - s34);
            if (handover <= 0.01d)
            {
                Debug.LogError($"[S3Figures] RESULT: FAIL - S3-4 is {s34End - s34:F2}s long, so " +
                               "there is no room in it for a dissolve.");
                return;
            }

            Debug.Log($"[S3Figures] S3-2 at {s32:F0}s, S3-3 at {s33:F0}s, S3-4 at {s34:F0}s " +
                      $"({s34End - s34:F0}s long); the S3 beats run out at {s3End:F0}s. Read off " +
                      $"Main's control tracks. Arrivals take {k_AppearSeconds:F0}s, the carried " +
                      $"objects starting {k_PropDelaySeconds:F0}s behind their 真人; S3-4's " +
                      $"handover {handover:F0}s.");

            // THE DELAYED ARRIVAL STILL HAS TO FINISH BEFORE THE HANDOVER. A re-cut that brought
            // S3-4 up close behind S3-3 would otherwise leave an object's arrival clip running
            // into its own departure clip, and the mixer's "last clip that has started wins"
            // would resolve that as a prop that half arrives and then snaps.
            double lastPropIn = System.Math.Max(s32, s33) + k_PropDelaySeconds + k_AppearSeconds;
            if (lastPropIn > s34)
            {
                Debug.LogError($"[S3Figures] RESULT: FAIL - an object cued {k_PropDelaySeconds:F0}s " +
                               $"behind its 真人 is still gathering at {lastPropIn:F0}s, after " +
                               $"S3-4 opens at {s34:F0}s. Shorten the delay or the arrival.");
                return;
            }

            // --- the two 真人 that arrive with things ---------------------------------------
            PointCloudFigure a4 = EnsureFigure($"{k_Content}/Figure_A4");
            PointCloudFigure b4 = EnsureFigure($"{k_Content}/Figure_B4");
            if (a4 == null || b4 == null) return;

            // --- the two that only one player each ever sees ---------------------------------
            PointCloudFigure a5 = EnsureFigure($"{k_Content}/Figure_A5", startHidden: true);
            PointCloudFigure b5 = EnsureFigure($"{k_Content}/Figure_B5", startHidden: true);
            if (a5 == null || b5 == null) return;

            // --- their props, in two kinds --------------------------------------------------
            var a4Props = new List<PropPresence>();
            var b4Props = new List<PropPresence>();
            if (!EnsureProps(k_A4Props, a4Props)) return;
            if (!EnsureProps(k_B4Props, b4Props)) return;

            var a4Clouds = new List<PointCloudFigure>();
            var b4Clouds = new List<PointCloudFigure>();
            if (!EnsureCloudProps(k_A4Clouds, a4Clouds)) return;
            if (!EnsureCloudProps(k_B4Clouds, b4Clouds)) return;

            // --- S3-2: Figure_A4 and its two objects gather, and go again at S3-4 -----------
            Arrive(timeline, director, a4, "S3-2 真人A4", s32, s34, handover);
            for (int i = 0; i < k_A4Props.Length; i++)
                Arrive(timeline, director, a4Props[i], k_A4Props[i].trackName,
                       s32 + k_PropDelaySeconds, s34, handover);
            for (int i = 0; i < k_A4Clouds.Length; i++)
                Arrive(timeline, director, a4Clouds[i], k_A4Clouds[i].trackName,
                       s32 + k_PropDelaySeconds, s34, handover);

            // --- S3-3: Figure_B4 and its two objects do the same, one beat later ------------
            Arrive(timeline, director, b4, "S3-3 真人B4", s33, s34, handover);
            for (int i = 0; i < k_B4Props.Length; i++)
                Arrive(timeline, director, b4Props[i], k_B4Props[i].trackName,
                       s33 + k_PropDelaySeconds, s34, handover);
            for (int i = 0; i < k_B4Clouds.Length; i++)
                Arrive(timeline, director, b4Clouds[i], k_B4Clouds[i].trackName,
                       s33 + k_PropDelaySeconds, s34, handover);

            // --- S3-4: each player is shown the other one's 真人 ----------------------------
            //
            // WHICH COLUMN A HEADSET READS IS THE WHOLE CONTENT OF THIS BEAT. FigureRoleDissolve
            // sends the machine whose local role matches 'figure' to the owner column and every
            // other machine to the other column, so 'figure' is set to THE PLAYER WHO SEES THIS
            // FIGURE -- 玩家A for Figure_B5 -- and only that column is ever given a cue. The
            // other column's cue sits out at k_Never, which the clip reads as fully gone for the
            // whole beat.
            //
            // Filling the owner column rather than the other one is also what keeps an
            // unconnected editor honest: with no session there is no role, and the clip falls
            // back to the owner column deliberately, so scrubbing S3-4 here shows both figures
            // instead of an empty room -- the same call O0FigureRoleView makes when it shows
            // both 真人 in O-0.
            CrossReveal(timeline, director, b5, "S3-4 真人B5（玩家A看見）",
                        PlayerRole.Player1, s34, s3End, handover);
            CrossReveal(timeline, director, a5, "S3-4 真人A5（玩家B看見）",
                        PlayerRole.Player2, s34, s3End, handover);

            // --- and S3 ends on white -------------------------------------------------------
            FadeToWhite(timeline, s3End, k_WhiteSeconds);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[S3Figures] RESULT: PASS");
        }

        // ------------------------------------------------------------------ scene side

        /// <summary>
        /// The 真人's driver, added if it is not there yet.
        ///
        /// <paramref name="startHidden"/> for the two figures whose cue is a
        /// <see cref="FigureRoleDissolveTrack"/> rather than a presence track. That track is
        /// plain clips with no mixer, so NOTHING answers for the figure before its clip opens --
        /// unlike the presence tracks, which are absent from frame 0 by themselves. An
        /// activation band covers it as well (see <see cref="CrossReveal"/>), and the authored 1
        /// here covers the single frame on which the band switches the object on, where the
        /// order of the two against each other is not ours to decide.
        ///
        /// The cost is that these two are invisible in the scene view until someone drags
        /// Dissolve back to 0 to place them. That is the right way round: a figure that is hard
        /// to see while authoring is a nuisance, and a figure standing in the room for twenty
        /// minutes of the show is a bug.
        /// </summary>
        static PointCloudFigure EnsureFigure(string path, bool startHidden = false)
        {
            GameObject go = Find(path);
            if (go == null)
            {
                Debug.LogError($"[S3Figures] RESULT: FAIL - no {path} in MainScene.");
                return null;
            }

            // Figure_A5 ships switched off. It has to be on for its component to run at all --
            // what hides it is the dissolve and the activation band, not the object's own flag.
            if (!go.activeSelf)
            {
                go.SetActive(true);
                Debug.Log($"[S3Figures] {go.name}: was inactive, switched on -- its cue hides it " +
                          "now, not activeSelf.");
            }

            var figure = go.GetComponent<PointCloudFigure>();
            if (figure == null)
            {
                figure = Undo.AddComponent<PointCloudFigure>(go);
                Debug.Log($"[S3Figures] {go.name}: PointCloudFigure added.");
            }

            if (startHidden)
            {
                var so = new SerializedObject(figure);
                SerializedProperty dissolve = so.FindProperty("m_Dissolve");
                if (dissolve != null && dissolve.floatValue < 0.999f)
                {
                    dissolve.floatValue = 1f;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log($"[S3Figures] {go.name}: authored Dissolve set to 1, so it is not " +
                              "in the room before its cue.");
                }
            }

            return figure;
        }

        /// <summary>
        /// Each prop switched onto a clippable material of its own and given a
        /// <see cref="PropPresence"/> in Clip mode.
        /// </summary>
        static bool EnsureProps(Prop[] props, List<PropPresence> into)
        {
            foreach (Prop prop in props)
            {
                GameObject go = Find(prop.path);
                if (go == null)
                {
                    Debug.LogError($"[S3Figures] RESULT: FAIL - no {prop.path} in MainScene.");
                    return false;
                }

                Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    Debug.LogError($"[S3Figures] RESULT: FAIL - nothing renders under " +
                                   $"{prop.path}, so there is nothing to dissolve.");
                    return false;
                }

                Material clippable = BuildClippable(prop.sourceMaterial, prop.materialPath);
                if (clippable == null) return false;

                // EVERY renderer under the prop, which for the T-shirt is both LODs. A LOD left
                // on the shared opaque material would snap back to solid as the player walks up
                // to a half-dissolved shirt.
                foreach (Renderer r in renderers)
                {
                    if (r.sharedMaterial == clippable) continue;
                    r.sharedMaterial = clippable;
                    EditorUtility.SetDirty(r);
                }

                var presence = go.GetComponent<PropPresence>();
                if (presence == null)
                {
                    presence = Undo.AddComponent<PropPresence>(go);
                    Debug.Log($"[S3Figures] {go.name}: PropPresence added.");
                }

                // CLIP, NOT FADE, and the room has already settled this twice: an alpha-blended
                // material is in neither the depth buffer nor _CameraOpaqueTexture, so KWS's
                // passes drop it out of the scene entirely. S3 has no water of its own, but the
                // cartons, the lamp and the coat all dissolve by clipping and a prop that faded
                // instead would read differently for no reason.
                var so = new SerializedObject(presence);
                SerializedProperty mode = so.FindProperty("m_Mode");
                if (mode != null) mode.enumValueIndex = (int)PropPresence.Mode.Clip;
                so.ApplyModifiedPropertiesWithoutUndo();

                into.Add(presence);
                Debug.Log($"[S3Figures] {go.name}: {renderers.Length} renderer(s) on " +
                          $"{clippable.name}, clipped by PropPresence.");
            }

            return true;
        }

        /// <summary>
        /// Each cloud prop wearing its bake, with the mesh it replaces switched off behind it.
        ///
        /// THE CLOUD GOES ON A CHILD, NOT ON THE PROP ITSELF, and the child is left at identity.
        /// Two reasons. The t-shirt's geometry is a LODGroup with two renderers under it, so
        /// there is no single renderer on the prop to take the cloud anyway; and the bake is
        /// written in THE PROP'S OWN LOCAL SPACE, which an identity child shares exactly -- so
        /// the points land on top of the mesh they were sampled from, and the shader's dissolve
        /// origin means the same thing as the authored one on the prop.
        ///
        /// THE MESH IS SWITCHED OFF, NOT DELETED. It is the only record in the scene of where
        /// this prop came from and the only thing a re-bake can read, and a disabled renderer
        /// costs a frame nothing. It does cost the bake, though: see <see cref="k_A4Clouds"/>.
        /// </summary>
        static bool EnsureCloudProps(CloudProp[] props, List<PointCloudFigure> into)
        {
            foreach (CloudProp prop in props)
            {
                GameObject go = Find(prop.path);
                if (go == null)
                {
                    Debug.LogError($"[S3Figures] RESULT: FAIL - no {prop.path} in MainScene.");
                    return false;
                }

                Mesh cloud = LoadCloud(prop.cloud);
                if (cloud == null)
                {
                    Debug.LogError($"[S3Figures] RESULT: FAIL - no point cloud at {prop.cloud}. " +
                                   "It is a bake, not a source asset -- see k_A4Clouds for how " +
                                   "to make it again.");
                    return false;
                }

                var material = AssetDatabase.LoadAssetAtPath<Material>(prop.material);
                if (material == null)
                {
                    Debug.LogError($"[S3Figures] RESULT: FAIL - no material at {prop.material}, " +
                                   "so the cloud would render pink.");
                    return false;
                }

                // --- the cloud itself -------------------------------------------------------
                string childName = go.name + "_Points";
                Transform child = go.transform.Find(childName);
                if (child == null)
                {
                    var made = new GameObject(childName);
                    Undo.RegisterCreatedObjectUndo(made, "S3 cloud prop");
                    child = made.transform;
                    child.SetParent(go.transform, false);
                    Debug.Log($"[S3Figures] {go.name}: {childName} added.");
                }

                child.localPosition = Vector3.zero;
                child.localRotation = Quaternion.identity;
                child.localScale = Vector3.one;
                if (!child.gameObject.activeSelf) child.gameObject.SetActive(true);

                // The prop's own layer. A cloud on a layer the headset's camera mask does not
                // list is invisible in the build and perfectly fine in the editor, which is a
                // bug this show has already shipped once.
                child.gameObject.layer = go.layer;

                var filter = child.GetComponent<MeshFilter>();
                if (filter == null) filter = Undo.AddComponent<MeshFilter>(child.gameObject);
                filter.sharedMesh = cloud;
                EditorUtility.SetDirty(filter);

                var renderer = child.GetComponent<MeshRenderer>();
                if (renderer == null) renderer = Undo.AddComponent<MeshRenderer>(child.gameObject);
                renderer.sharedMaterial = material;
                EditorUtility.SetDirty(renderer);

                // --- and the mesh it stands in for ------------------------------------------
                int hidden = HideSourceGeometry(go, child, prop.sourceMaterial);

                // PropPresence is the other mechanism. Left on, it is a component nothing drives
                // -- its track is about to be replaced by a FigurePresenceTrack of the same name
                // -- and the next reader has to work out which of the two is live.
                var stale = go.GetComponent<PropPresence>();
                if (stale != null)
                {
                    Undo.DestroyObjectImmediate(stale);
                    Debug.Log($"[S3Figures] {go.name}: PropPresence removed -- it dissolves as a " +
                              "cloud now.");
                }

                var figure = go.GetComponent<PointCloudFigure>();
                if (figure == null)
                {
                    figure = Undo.AddComponent<PointCloudFigure>(go);
                    Debug.Log($"[S3Figures] {go.name}: PointCloudFigure added.");
                }

                TuneCloudProp(figure, renderer, go.transform, cloud.bounds);

                into.Add(figure);
                Debug.Log($"[S3Figures] {go.name}: {cloud.vertexCount / 4:N0} points on " +
                          $"{material.name}, {hidden} source renderer(s) switched off.");
            }

            return true;
        }

        /// <summary>The mesh a .ply imported into, or null.</summary>
        static Mesh LoadCloud(string path)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is Mesh mesh) return mesh;
            return null;
        }

        /// <summary>
        /// The modelled prop behind the cloud, taken out of the frame.
        ///
        /// THE LODGROUP HAS TO GO FIRST, and switching the renderers off is not enough on its
        /// own: a LODGroup owns the enabled flag of every renderer it lists and writes it again
        /// the moment the camera moves, so the t-shirt would come back the first time anybody
        /// walked towards it -- in the build, not in the editor, where nothing is culling.
        /// </summary>
        static int HideSourceGeometry(GameObject prop, Transform cloud, string sourceMaterial)
        {
            foreach (LODGroup group in prop.GetComponentsInChildren<LODGroup>(true))
            {
                if (!group.enabled) continue;
                group.enabled = false;
                EditorUtility.SetDirty(group);
            }

            var stock = AssetDatabase.LoadAssetAtPath<Material>(sourceMaterial);
            int hidden = 0;

            foreach (Renderer r in prop.GetComponentsInChildren<Renderer>(true))
            {
                if (r.transform == cloud || r.transform.IsChildOf(cloud)) continue;

                if (stock != null && r.sharedMaterial != stock)
                {
                    r.sharedMaterial = stock;
                    EditorUtility.SetDirty(r);
                }

                if (!r.enabled) continue;
                r.enabled = false;
                EditorUtility.SetDirty(r);
                hidden++;
            }

            return hidden;
        }

        /// <summary>
        /// A prop cloud's idle and its departure, in the room's metres rather than the object's.
        ///
        /// DIVIDED BY THE PROP'S SCALE, every one of them, because the shader works in the
        /// cloud's object space and these two props are not at scale 1: the t-shirt is at 0.8
        /// and the stone at 3. One authored pair of numbers would otherwise mean two different
        /// looks, and the stone -- the one furthest from 1 -- would get three times the shimmer
        /// and three times the drift of the shirt beside it.
        ///
        /// THE DRIFT DIRECTION IS WORLD UP, EXPRESSED LOCALLY. _DissolveDir is an object-space
        /// vector, and the t-shirt is lying at (90, 157.8, 0) -- its own up points across the
        /// room. Authored as a plain Vector3.up the shirt's points would leave sideways while
        /// the 真人 next to it rose, which is the one thing these two cues must not do.
        ///
        /// NO REVEAL RADIUS. The presence track writes _Dissolve and nothing else (see
        /// <see cref="FigurePresenceBehaviour"/>: an arrival is the dissolve running backwards),
        /// so _Reveal stays at the authored 1 forever -- and at 1 a non-zero radius still fades
        /// out the points furthest from the reveal origin. The bake's prefab sets one; this
        /// clears it.
        /// </summary>
        static void TuneCloudProp(PointCloudFigure figure, Renderer cloud, Transform prop,
                                  Bounds bounds)
        {
            Vector3 lossy = prop.lossyScale;
            float scale = Mathf.Max(1e-4f, (Mathf.Abs(lossy.x) + Mathf.Abs(lossy.y) + Mathf.Abs(lossy.z)) / 3f);

            var so = new SerializedObject(figure);

            SerializedProperty bound = so.FindProperty("m_Cloud");
            if (bound != null) bound.objectReferenceValue = cloud;

            SetFloat(so, "m_Turbulence", k_CloudTurbulence / scale);
            SetFloat(so, "m_TurbulenceScale", k_CloudGrain * scale);

            SetFloat(so, "m_Reveal", 1f);
            SetFloat(so, "m_RevealRadius", 0f);
            SetVector(so, "m_RevealFrom", Vector3.zero);

            // Out of the middle of the object and outwards, which on something this size is the
            // only reading of 'comes apart' that is not just a translation.
            SetVector(so, "m_DissolveFrom", bounds.center);
            SetFloat(so, "m_DissolveRadius", bounds.extents.magnitude);
            SetFloat(so, "m_DissolveDrift", k_CloudDrift / scale);
            SetFloat(so, "m_DissolveSpread", k_CloudSpread / scale);
            SetVector(so, "m_DissolveDir", prop.InverseTransformDirection(Vector3.up).normalized);

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetFloat(SerializedObject so, string property, float value)
        {
            SerializedProperty p = so.FindProperty(property);
            if (p != null) p.floatValue = value;
        }

        static void SetVector(SerializedObject so, string property, Vector3 value)
        {
            SerializedProperty p = so.FindProperty(property);
            if (p != null) p.vector3Value = value;
        }

        /// <summary>
        /// A copy of a prop's own material, made clippable.
        ///
        /// COPIED FROM THE NAMED SOURCE EVERY RUN rather than patched in place, which is what
        /// makes this idempotent: MakePropClippable reads the material's _BaseMap and writes the
        /// noise map back into that same slot, so a run over an already-clipped material builds
        /// a dissolve map OF a dissolve map. Re-copying from the model's own material puts the
        /// real base map back first. BuildDissolveMap leaves an existing map alone, so the
        /// pattern never re-rolls and the props come apart the same way they did yesterday.
        /// </summary>
        static Material BuildClippable(string sourcePath, string path)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            if (source == null)
            {
                Debug.LogError($"[S3Figures] RESULT: FAIL - no source material at {sourcePath}.");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source.shader);
                AssetDatabase.CreateAsset(material, path);
                Debug.Log($"[S3Figures] {path} created from {source.name}.");
            }

            material.shader = source.shader;
            material.CopyPropertiesFromMaterial(source);
            material.shaderKeywords = source.shaderKeywords;
            EditorUtility.SetDirty(material);

            return BuildO1Timeline.MakePropClippable(path)
                ? AssetDatabase.LoadAssetAtPath<Material>(path)
                : null;
        }

        // --------------------------------------------------------------- timeline side

        /// <summary>
        /// A 真人 that gathers at <paramref name="inAt"/> and comes apart at
        /// <paramref name="outAt"/>, seen the same way by both players.
        ///
        /// Two short clips rather than one long one. The mixer holds the value the last clip
        /// that has started ended on, so eight seconds of arrival at the cue and eight of
        /// departure at S3-4 is the whole schedule -- and the gap between them is the figure
        /// simply standing there, which costs nothing to author and nothing to re-time.
        /// </summary>
        static void Arrive(TimelineAsset timeline, PlayableDirector director, PointCloudFigure figure,
                           string trackName, double inAt, double outAt, double outSeconds)
        {
            DropSuperseded<FigurePresenceTrack>(timeline, director, trackName);
            FigurePresenceTrack track = Rebuild<FigurePresenceTrack>(timeline, trackName);

            // ABSENT UNTIL CUED. Without this the mixer answers 0 before the first clip anyway
            // -- it is this track's default -- but it is set explicitly because it is the one
            // thing standing between Figure_A4 and twenty minutes in the room.
            track.presenceBeforeFirstClip = 0f;

            Curve(track, inAt, k_AppearSeconds, 0f, 1f, "聚合");
            Curve(track, outAt, outSeconds, 1f, 0f, "散去");

            director.SetGenericBinding(track, figure);
            Dirty(timeline, director);

            Debug.Log($"[S3Figures] '{trackName}' -> {figure.name}: in at {inAt:F0}s, out at " +
                      $"{outAt:F0}s.");
        }

        /// <summary>As above, for a prop group rather than a cloud.</summary>
        static void Arrive(TimelineAsset timeline, PlayableDirector director, PropPresence prop,
                           string trackName, double inAt, double outAt, double outSeconds)
        {
            DropSuperseded<PropPresenceTrack>(timeline, director, trackName);
            PropPresenceTrack track = Rebuild<PropPresenceTrack>(timeline, trackName);

            // AND HERE IT IS NOT THE DEFAULT. PropPresenceTrack answers 1 before its first clip,
            // because the first thing it was ever put on was a wall that is part of the room
            // until something dissolves it. A carton that ARRIVES needs the opposite, or it
            // stands in S1 and S2 and then dissolves in on top of itself.
            track.presenceBeforeFirstClip = 0f;

            PropCurve(track, inAt, k_AppearSeconds, 0f, 1f, "聚合");
            PropCurve(track, outAt, outSeconds, 1f, 0f, "散去");

            director.SetGenericBinding(track, prop);
            Dirty(timeline, director);

            Debug.Log($"[S3Figures] '{trackName}' -> {prop.name}: in at {inAt:F0}s, out at " +
                      $"{outAt:F0}s.");
        }

        /// <summary>
        /// A 真人 only one of the two players ever sees, gathering at <paramref name="at"/>.
        ///
        /// The role clip covers the beat and the rest of S3, so the figure stays once it has
        /// arrived -- nothing in the script says when these two leave, and holding them is the
        /// reading that does not invent a departure. The activation band beside it is what keeps
        /// the object out of the room for the 21 minutes before the cue: an activation track
        /// forces its target inactive everywhere it has no clip, which is the same thing
        /// BuildS3Orbits leans on to hide the S3 footprints through all of O, S1 and S2.
        /// </summary>
        static void CrossReveal(TimelineAsset timeline, PlayableDirector director,
                                PointCloudFigure figure, string trackName, PlayerRole seenBy,
                                double at, double until, double appearSeconds)
        {
            FigureRoleDissolveTrack track = Rebuild<FigureRoleDissolveTrack>(timeline, trackName);

            TimelineClip clip = track.CreateClip<FigureRoleDissolveClip>();
            clip.start = at;
            clip.duration = System.Math.Max(appearSeconds + 1d, until - at);
            clip.displayName = trackName;

            var asset = (FigureRoleDissolveClip)clip.asset;
            FigureRoleDissolveBehaviour shape = asset.template;

            shape.figure = seenBy;
            shape.appearSeconds = (float)appearSeconds;
            shape.leaveSeconds = k_FinalOutSeconds;

            // The player this column belongs to: gathers as the beat opens, stands through S3-5
            // and S3-6, and comes apart across the last ten seconds of the section. Measured
            // from the clip's own end rather than typed in, so it stays the LAST ten seconds.
            shape.ownerAppearsAt = 0f;
            shape.ownerLeavesAt = (float)(clip.duration - k_FinalOutSeconds);

            // The other one: never arrives at all, so it has nothing to leave.
            shape.otherAppearsAt = k_Never;
            shape.otherLeavesAt = k_Never;

            if (shape.ownerLeavesAt <= appearSeconds)
            {
                Debug.LogError($"[S3Figures] RESULT: FAIL - '{trackName}' is only " +
                               $"{clip.duration:F0}s long, so a {appearSeconds:F0}s arrival and a " +
                               $"{k_FinalOutSeconds:F0}s departure do not both fit in it.");
                return;
            }

            EditorUtility.SetDirty(asset);
            director.SetGenericBinding(track, figure);

            // And the band that keeps it out of the room beforehand.
            ActivationTrack band = Rebuild<ActivationTrack>(timeline, $"{trackName} 進出");
            TimelineClip up = band.CreateDefaultClip();
            up.start = at;
            up.duration = clip.duration;
            up.displayName = trackName;
            band.postPlaybackState = ActivationTrack.PostPlaybackState.Active;
            director.SetGenericBinding(band, figure.gameObject);

            Dirty(timeline, director);

            Debug.Log($"[S3Figures] '{trackName}' -> {figure.name}: gathers at {at:F0}s for " +
                      $"{seenBy} only, then comes apart from " +
                      $"{at + shape.ownerLeavesAt:F0}s and is gone by {at + clip.duration:F0}s.");
        }

        /// <summary>
        /// The screen going white across the last seconds of S3.
        ///
        /// WHITE, NOT BLACK, and <see cref="ScreenFadeBehaviour.color"/> is all that takes --
        /// the fade is a quad in front of the camera tinted by whatever the playing clip asks
        /// for, so a white clip costs exactly what the blackouts cost. The colour is written
        /// every frame by whichever clip is running, and the fade's material is created at
        /// runtime rather than being a project asset, so leaving the quad white at the end of
        /// the show cannot write white back into the project.
        ///
        /// A RISING CURVE, NOT AN OUT-HOLD-IN like the three 黑場 clips. Those are transitions
        /// between two sections and have to lift again on the other side; this is the end of
        /// S3, so the end value is held and the screen simply stays white. Nothing follows it
        /// on the timeline today.
        ///
        /// NO LIGHTING SWITCH. The 黑場 clips carry LightingDirector looks because a blackout is
        /// the only moment a bake can be swapped unseen; there is no section after this one to
        /// switch to, and naming a look here would warn about a switch happening at an alpha
        /// that is not yet 1.
        /// </summary>
        static void FadeToWhite(TimelineAsset timeline, double endsAt, double seconds)
        {
            PlayableTrack track = null;
            foreach (TrackAsset t in timeline.GetOutputTracks())
            {
                if (t is PlayableTrack found && found.name == k_FadeTrack)
                {
                    track = found;
                    break;
                }
            }

            if (track == null)
            {
                Debug.LogError($"[S3Figures] RESULT: FAIL - no '{k_FadeTrack}' track on Main, so " +
                               "there is nowhere to put the fade. It is the track the three " +
                               "黑場 clips are on.");
                return;
            }

            // BY NAME, AND ONLY OURS. See k_FadeTrack: the 黑場 clips share this track and carry
            // the lighting switches, so clearing it the way the figures' own tracks are cleared
            // would quietly cost the show its three section transitions.
            int removed = 0;
            foreach (TimelineClip old in new List<TimelineClip>(track.GetClips()))
            {
                if (old.displayName != k_WhiteClip) continue;
                track.DeleteClip(old);
                removed++;
            }

            TimelineClip clip = track.CreateClip<ScreenFadeClip>();
            clip.start = endsAt - seconds;
            clip.duration = seconds;
            clip.displayName = k_WhiteClip;

            var asset = (ScreenFadeClip)clip.asset;
            asset.name = k_WhiteClip;
            ScreenFadeBehaviour shape = asset.template;
            shape.color = Color.white;
            shape.alpha = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            shape.lightingLook = "";
            shape.lightingLookBefore = "";
            EditorUtility.SetDirty(asset);
            EditorUtility.SetDirty(timeline);

            Debug.Log($"[S3Figures] '{k_WhiteClip}' on '{k_FadeTrack}': white from " +
                      $"{clip.start:F0}s to {endsAt:F0}s" +
                      $"{(removed > 0 ? $", replacing {removed} earlier copy" : "")}. " +
                      $"{CountOthers(track)} other clip(s) on that track left alone.");
        }

        /// <summary>How many clips on the fade track are not this builder's, for the log.</summary>
        static int CountOthers(TrackAsset track)
        {
            int n = 0;
            foreach (TimelineClip c in track.GetClips())
                if (c.displayName != k_WhiteClip) n++;
            return n;
        }

        /// <summary>One presence block on a figure track.</summary>
        static void Curve(FigurePresenceTrack track, double at, double seconds, float from, float to,
                          string name)
        {
            TimelineClip clip = track.CreateClip<FigurePresenceClip>();
            clip.start = at;
            clip.duration = seconds;
            clip.displayName = name;

            var asset = (FigurePresenceClip)clip.asset;
            asset.template.presence = AnimationCurve.EaseInOut(0f, from, 1f, to);
            EditorUtility.SetDirty(asset);
        }

        /// <summary>One presence block on a prop track.</summary>
        static void PropCurve(PropPresenceTrack track, double at, double seconds, float from,
                              float to, string name)
        {
            TimelineClip clip = track.CreateClip<PropPresenceClip>();
            clip.start = at;
            clip.duration = seconds;
            clip.displayName = name;

            var asset = (PropPresenceClip)clip.asset;
            asset.template.presence = AnimationCurve.EaseInOut(0f, from, 1f, to);
            EditorUtility.SetDirty(asset);
        }

        /// <summary>
        /// The named track, emptied of its clips, or a new one.
        ///
        /// Emptied rather than added to, for the reason BuildS3Orbits gives about its activation
        /// band: running a builder twice is not meant to be a way of discovering that a track
        /// now has two arrivals on it.
        /// </summary>
        /// <summary>
        /// A track of this name that is not the kind being authored, taken off Main.
        ///
        /// BECAUSE A PROP CAN CHANGE MECHANISM. 'S3-2 A4 T恤' was a PropPresenceTrack until the
        /// t-shirt became a point cloud, and <see cref="Rebuild{T}"/> matches on name AND type:
        /// without this it would leave the old track exactly where it is, with its two clips and
        /// its binding, and add a second track beside it under the same name. The show would
        /// have one cue written twice, one of them onto a component that is no longer there, and
        /// the running order would read as though somebody had run the builder twice.
        ///
        /// The binding goes with it. A director keeps bindings keyed on the track asset, and a
        /// deleted track leaves an entry pointing at nothing that the next scene save writes out.
        /// </summary>
        static void DropSuperseded<T>(TimelineAsset timeline, PlayableDirector director, string name)
            where T : TrackAsset
        {
            foreach (TrackAsset t in new List<TrackAsset>(timeline.GetOutputTracks()))
            {
                if (t == null || t.name != name || t is T) continue;

                string kind = t.GetType().Name;
                director.ClearGenericBinding(t);
                timeline.DeleteTrack(t);
                EditorUtility.SetDirty(timeline);
                EditorUtility.SetDirty(director);

                Debug.Log($"[S3Figures] '{name}': a {kind} of the same name was how this cue used " +
                          "to be driven; removed, so the new one is the only track with that name.");
            }
        }

        static T Rebuild<T>(TimelineAsset timeline, string name) where T : TrackAsset, new()
        {
            T track = null;
            foreach (TrackAsset t in timeline.GetOutputTracks())
            {
                if (t is T found && found.name == name)
                {
                    track = found;
                    break;
                }
            }

            if (track == null) return timeline.CreateTrack<T>(null, name);

            foreach (TimelineClip old in new List<TimelineClip>(track.GetClips()))
                track.DeleteClip(old);

            return track;
        }

        static void Dirty(TimelineAsset timeline, PlayableDirector director)
        {
            EditorUtility.SetDirty(timeline);
            EditorUtility.SetDirty(director);
        }

        // ------------------------------------------------------------------- the show

        static PlayableDirector FindShowDirector(TimelineAsset timeline)
        {
            foreach (PlayableDirector d in Object.FindObjectsByType<PlayableDirector>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (d.playableAsset == timeline) return d;
            }

            return null;
        }

        /// <summary>
        /// When a beat starts and ends, from its control track on Main.
        ///
        /// The name is trimmed before it is compared: the S3-3 track is called 'S3-3 ' with a
        /// trailing space, and a builder that missed it over one character would report a
        /// missing beat for a beat that is plainly there.
        /// </summary>
        static bool TryBeat(TimelineAsset timeline, string beat, out double from, out double to)
        {
            from = 0d;
            to = 0d;

            foreach (TrackAsset t in timeline.GetOutputTracks())
            {
                if (t is not ControlTrack || t.name.Trim() != beat) continue;

                foreach (TimelineClip c in t.GetClips())
                {
                    from = c.start;
                    to = c.end;
                    return true;
                }

                Debug.LogError($"[S3Figures] RESULT: FAIL - the '{beat}' control track on Main " +
                               "has no clip on it, so when that beat happens is not knowable.");
                return false;
            }

            Debug.LogError($"[S3Figures] RESULT: FAIL - no '{beat}' control track on Main. The S3 " +
                           "running order is not what this builder was written against.");
            return false;
        }

        /// <summary>The end of the last S3 beat, which is how long the two S5 figures are held.</summary>
        static double LastS3End(TimelineAsset timeline)
        {
            double end = 0d;
            foreach (TrackAsset t in timeline.GetOutputTracks())
            {
                if (t is not ControlTrack || !t.name.Trim().StartsWith("S3-")) continue;
                foreach (TimelineClip c in t.GetClips())
                    if (c.end > end) end = c.end;
            }

            return end;
        }

        /// <summary>
        /// GameObject.Find by path, through inactive objects too -- which it has to be: Figure_A5
        /// ships switched off, and Beat_S3 itself is as likely as not driven inactive while this
        /// runs.
        /// </summary>
        static GameObject Find(string path)
        {
            string[] parts = path.Split('/');

            Transform cursor = null;
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name != parts[0]) continue;
                cursor = root.transform;
                break;
            }

            for (int i = 1; i < parts.Length && cursor != null; i++) cursor = cursor.Find(parts[i]);
            return cursor != null ? cursor.gameObject : null;
        }
    }
}
