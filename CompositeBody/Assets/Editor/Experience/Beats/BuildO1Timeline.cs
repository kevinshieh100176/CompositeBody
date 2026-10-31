using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using CompositeBody.Experience;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// O-1 鬼的控制 gets a timeline of its own, and everything the beat shows arrives and leaves
    /// on it.
    ///
    /// 「需要擺放的物件跟箱子以及 Showcase_CB_boxes 會 dissolve in（做在 o_1 timeline 上）... 當兩個玩家
    /// 都把所有該擺放的物件都放完、音檔也都撥放完畢後，會 dissolve out，然後開始 O-2」.
    ///
    /// ONE CLIP, FIVE CURVES, so the pacing is in one place. The coat, the mug, the two cartons
    /// and the wall of boxes behind them each get a <see cref="PropPresence"/>, and a single
    /// animation clip spanning the beat drives all five. Re-time it by dragging its keys in the
    /// Animation window, or by changing the four numbers below and running this again.
    ///
    /// NOT TWO SHORT CLIPS, which would read better in the Timeline window and would not work:
    /// an animation track reverts the properties it drives to their authored values the moment
    /// its clip ends, so a dissolve-in clip that stopped at seven seconds would take the boxes
    /// away again on the eighth. O-0's handover gets away with two short clips because both
    /// components it drives latch; presence has to come back down, so it cannot.
    ///
    /// THE DISSOLVE-OUT IS THE TAIL OF THE BEAT, after the hold. The show's hold marker sits
    /// <see cref="k_OutLead"/> seconds before the end of O-1 rather than at the end of it, so the
    /// moment both players are finished the last seconds of this timeline play: the objects go,
    /// the boxes go, and O-2 starts in an empty room. That is what the holdLead column in
    /// BuildMainTimeline's running order is for.
    ///
    /// THE WALL OF BOXES MOVES INTO THE PREFAB. It was staged by hand in MainScene under
    /// Beat_O2, which is a holder the running order does not even reference any more -- so it
    /// was simply up, from the first frame of the show, through the whole of O-0. It belongs to
    /// this beat, and a beat's content lives in the onboarding prefab with the director that
    /// drives it, because a timeline binding is a reference to a scene object. Its transform is
    /// read off the scene instance first, so whatever was staged by hand is what it keeps.
    ///
    /// AND THE CARDBOARD DISSOLVES RATHER THAN FADING. The cartons are opaque on purpose -- a
    /// transparent carton sorted against its own flaps and let the 光圈 shine through its near
    /// wall, and URP re-derives a transparent material's depth settings on every import, so that
    /// cannot be patched out. Alpha clipping is the way to take an opaque thing away: the base
    /// map gets noise written into its alpha channel, the material clips against it, and raising
    /// the cutoff eats the box away in patches. Solid while it is there, gone in pieces, and the
    /// shadow pass clips with it.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildO1Timeline.Run
    /// </summary>
    public static class BuildO1Timeline
    {
        const string k_Timeline = "Assets/_Timeline/O_1.playable";
        const string k_Prefab = "Assets/_Scenes/Onboarding.prefab";
        const string k_MainScene = "Assets/_Scenes/MainScene.unity";

        /// <summary>The object inside the prefab that carries this beat's director.</summary>
        public const string DirectorName = "Timeline O_1";

        const string k_GrabGroup = "O_1 Grab";
        const string k_ShowcaseName = "Showcase_CB_boxes";
        const string k_SpeakerName = "Cue O-1";
        const string k_WaveSpeakerName = "Cue O-1 Waves";
        const string k_TrackName = "O-1: dissolve";
        const string k_ClipName = "O_1_Dissolve";

        const string k_ShowcaseModel =
            "Assets/_models/set-of-cardboard-boxes/source/Showcase_CB_boxes.fbx";
        const string k_ShowcaseSourceMat =
            "Assets/_models/set-of-cardboard-boxes/source/Cardboard_Box_Mat.mat";
        const string k_ShowcaseMat = "Assets/Materials/O1ShowcaseBoxes.mat";
        const string k_ShowcaseBase =
            "Assets/_models/set-of-cardboard-boxes/textures/Cardboard_boxes_1_BaseColor.png";
        const string k_ShowcaseDissolve =
            "Assets/_models/set-of-cardboard-boxes/textures/Cardboard_boxes_1_BaseColor_Dissolve.png";

        const string k_CartonMat = "Assets/Materials/O1Cardboard.mat";
        const string k_CartonBase =
            "Assets/_models/3d-cardboard-lowpoly/textures/Cardboard_MAT_Base_color.png";
        const string k_CartonDissolve =
            "Assets/_models/3d-cardboard-lowpoly/textures/Cardboard_MAT_Base_color_Dissolve.png";

        /// <summary>
        /// THE DROP IS SILENT NOW. 「兩人必須都抓住物件才會觸發VO，要不然只有音效」 -- the speech answers
        /// both players HOLDING their object, which is <see cref="k_WaveLines"/> below; putting
        /// it away is the quiet half. This list used to hold O-1AB_紀念日快樂 and is deliberately
        /// empty rather than deleted: <see cref="O1DropSequence"/> still runs the beat's gate,
        /// and a line per drop is one entry away if the running order asks for it again.
        /// </summary>
        static readonly string[] k_Lines = { };

        /// <summary>
        /// O-1's three sub-beats, in order, and what is said at the end of each. v4 writes them
        /// as three rows -- 鬼的抓取1 燈 85s, 鬼的抓取2 T-shirt 35s, 封箱 50s -- but they are one
        /// beat: 「播完後下一個物件才會出現」, so each begins when the players finished the last and
        /// none of them has a time of its own.
        ///
        /// TWO KINDS OF LINE. The first two are 「與B共同聆聽」 -- one recording, both players,
        /// same moment -- and the third is not: 封箱 gives each player their own, O-A3 and O-B3.
        /// <see cref="O1GrabWaves"/> takes either, and times the chain off the longer of a pair
        /// so the next object still arrives for both of them on the same frame.
        ///
        /// THE THIRD IS A PAUSE, AND IT IS MARKED AS ONE. 「這裡先不要加互動 這邊是停頓點」 -- 封箱 is
        /// a line with no object behind it, so it is not waiting on a grab that was never built:
        /// it speaks as soon as T-shirt's line ends, which is 「兩人都抓過兩樣東西 讓2段vo都播完」, and
        /// the show stays on O-1's hold until it is over. The flag is what tells that apart from
        /// a wave whose objects are simply missing, which is still worth a warning.
        ///
        /// THE NAMES ARE ASCII because they reach the debug panel through O1GrabWaves' row, and
        /// the panel's font has no CJK glyphs -- see AvatarFormationSync.debugLabel. The running
        /// order's own names for these three are 燈, T shirt and 封箱.
        /// </summary>
        static readonly (string name, string shared, string a, string b, bool pause)[] k_WaveLines =
        {
            ("Lamp", "Assets/_Audio/O-AB1_睡前.wav", null, null, false),
            ("Jacket", "Assets/_Audio/O-AB2_搶衣服.wav", null, null, false),
            ("Seal the box", null, "Assets/_Audio/O-A3_有些東西可以放進箱子.wav",
                                   "Assets/_Audio/O-B3_有些東西可以放進箱子.wav", true),
        };

        /// <summary>
        /// O-1's authored length, from the running order in BuildMainTimeline. 170s is v4's
        /// three sub-beats added up -- 燈 85, T-shirt 35, 封箱 50 -- and not three clips: where
        /// inside it each object appears is decided by the players, not by this number. It only
        /// has to be WIDE ENOUGH, because a Control clip drives its sub-director for exactly as
        /// long as the clip is.
        /// </summary>
        const double k_BeatSeconds = 170d;

        // THE PACING. Four numbers, and the only four worth having opinions about.
        //
        // The first is not zero: the show arrives here off O-0's hold and a dissolve that started
        // on the same instant would have been running while the players were still being waited
        // for.
        const double k_InAt = 1.5d;        // the cartons and the wall begin to arrive
        const double k_In = 6d;            // over six seconds: 1.5 -> 7.5

        // AND THEN THE OBJECTS, which did not used to be a separate pair of numbers: everything
        // arrived on one curve. 「Showcase_CB_boxes 跟箱子(Carton)...出現後 燈會出現」 -- the boxes
        // first, the lamps once there is somewhere for them to go. A lamp that dissolved in
        // alongside its carton was a room filling up; one that arrives after it is an
        // instruction, and the beat never has to say a word.
        const double k_PropAt = 8d;        // half a second after the cartons have finished
        const double k_PropIn = 4d;        // over four seconds: 8 -> 12

        /// <summary>
        /// How much of the beat is left after the hold -- which is to say how long the
        /// dissolve-out has to run in. BuildMainTimeline puts the hold marker this far before the
        /// end of O-1, so these are the seconds that play once both players are finished.
        /// </summary>
        public const double k_OutLead = 9d;

        /// <summary>
        /// Where the hold sits, which is the only thing that is left of what used to be the
        /// dissolve-out. The out-curve that ran from here is gone -- see <see cref="Curve"/>;
        /// O-2 is what empties the room now. The constant stays because the log says where the
        /// show stops to wait, and because <see cref="k_OutLead"/> is still the gap that
        /// BuildMainTimeline puts between the marker and the end of the beat.
        /// </summary>
        static double outAt => k_BeatSeconds - k_OutLead;

        /// <summary>
        /// How fine the dissolve pattern is, in cells across the base map. Low numbers eat the
        /// box away in a few big patches, high numbers fizz it out evenly -- the 真人's own
        /// dissolve has a scatter control for the same reason.
        /// </summary>
        const int k_NoiseCells = 24;

        /// <summary>
        /// Resolution of the generated base maps. The sources are 2048 and one of them is a
        /// 25 MB PNG; a copy at that size with an alpha channel would be bigger again, for a
        /// cardboard texture nobody looks at closely. 1024 is 2.5 texels per millimetre on a
        /// carton held at arm's length.
        /// </summary>
        const int k_MapSize = 1024;

        [MenuItem("Tools/O-1/Build O-1 Timeline")]
        public static void RunFromMenu()
        {
            // Run() opens MainScene, which discards whatever is unsaved right now.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run();
        }

        public static void Run()
        {
            Debug.Log("[O1Timeline] Starting...");

            Material carton = BuildCartonMaterial();
            Material showcase = BuildShowcaseMaterial();
            if (carton == null || showcase == null) return;

            // The grabbable objects get the same treatment as the cartons: opaque, clipped
            // against noise in their own base map's alpha. BuildO1Grab makes and tints them;
            // how this beat dissolves is one thing and it is owned here.
            foreach (BuildO1Grab.Prop prop in BuildO1Grab.Props)
            {
                if (!MakePropClippable(prop.Material(PlayerRole.Player1))) return;
                if (!MakePropClippable(prop.Material(PlayerRole.Player2))) return;
            }

            List<AudioClip> lines = LoadLines(k_Lines);
            if (lines == null) return;

            // The wall of boxes, as it was staged by hand. Taken out of the scene here; put back
            // inside the prefab below, so it travels with the beat that owns it.
            if (!TakeShowcaseFromScene(out Matrix4x4 staged)) return;

            TimelineAsset timeline = LoadOrCreate();
            if (timeline == null) return;

            GameObject root = PrefabUtility.LoadPrefabContents(k_Prefab);
            if (root == null)
            {
                Debug.LogError($"[O1Timeline] RESULT: FAIL - could not open {k_Prefab}.");
                return;
            }

            try
            {
                Transform group = FindDeep(root.transform, k_GrabGroup);
                if (group == null)
                {
                    Debug.LogError($"[O1Timeline] RESULT: FAIL - no Beats/{k_GrabGroup} in the " +
                                   "prefab. Run Tools/Onboarding/Build Onboarding first.");
                    return;
                }

                Clear(timeline);

                PlayableDirector director = EnsureDirector(root.transform, timeline);
                Animator animator = EnsureAnimator(group);

                // Two groups and not one list, because they no longer arrive together: the wall
                // and the cartons come in first and the lamps follow them.
                var scenery = new List<string>();
                var props = new List<string>();

                Transform wall = EnsureShowcase(group, staged, showcase);
                if (wall == null) return;
                scenery.Add(Path(group, wall));

                var boxes = new List<(PlayerRole role, O1DropBox box)>();

                // Per object, the two stations that hold it -- which is what a wave is. The
                // first object's pair goes on the animation track; every later one is handed to
                // O1GrabWaves, so the two lists below are deliberately not the same thing.
                var byProp = new List<List<O1GrabStation>>();
                var presenceByProp = new List<List<PropPresence>>();

                for (int i = 0; i < BuildO1Grab.Props.Length; i++)
                {
                    BuildO1Grab.Prop prop = BuildO1Grab.Props[i];
                    bool onTrack = i == 0;

                    var held = new List<O1GrabStation>();
                    var presences = new List<PropPresence>();

                    foreach (PlayerRole role in new[] { PlayerRole.Player1, PlayerRole.Player2 })
                    {
                        string station = prop.Station(role);

                        Transform t = group.Find(station);
                        if (t == null)
                        {
                            Debug.LogError($"[O1Timeline] RESULT: FAIL - no '{station}' under " +
                                           $"{k_GrabGroup}. Run Tools/O-1/Build Grab " +
                                           "Interaction and Tools/O-1/Build the Drop Boxes " +
                                           "first.");
                            return;
                        }

                        if (!WireStation(group, t, role, carton, scenery,
                                         onTrack ? props : null, boxes)) return;

                        var grab = t.GetComponent<O1GrabStation>();
                        if (grab == null)
                        {
                            Debug.LogError($"[O1Timeline] RESULT: FAIL - '{station}' has no " +
                                           "O1GrabStation, so the beat cannot know when both " +
                                           "players are holding their object. Run Tools/O-1/" +
                                           "Wire Grab Cues and Payoff first.");
                            return;
                        }

                        held.Add(grab);
                        presences.Add(t.GetComponent<PropPresence>());
                    }

                    byProp.Add(held);
                    presenceByProp.Add(presences);
                }

                // EVERY OBJECT HAS A WATCHER, checked only now that every station has been
                // walked: all the drop watchers hang under the FIRST object's station -- there
                // is one carton per player and several things to put in it -- so the list is
                // not complete until the loop above is. The match is by the grab each watcher
                // is pointed at rather than by a name that could be renamed.
                //
                // A CHECK AND NOT A WIRING. Nothing in the wave chain waits on a box any more;
                // what the gate waits on is every object being put away, and an object with no
                // watcher is one that can never be. The beat would hold until its fallback and
                // nobody would know which object was the one nobody could finish.
                foreach (List<O1GrabStation> stations in byProp)
                {
                    foreach (O1GrabStation station in stations)
                    {
                        var grab = station.GetComponentInChildren<XRGrabInteractable>(true);
                        bool watched = false;

                        foreach ((PlayerRole role, O1DropBox drop) in boxes)
                        {
                            if (role != station.owner || drop.target != grab) continue;
                            watched = true;
                            break;
                        }

                        if (watched) continue;

                        Debug.LogError($"[O1Timeline] RESULT: FAIL - nothing under " +
                                       $"{station.owner}'s carton is watching " +
                                       $"'{station.name}', so it could never be put away and " +
                                       "the beat's gate could never clear. Run Tools/O-1/Build " +
                                       "the Drop Boxes first.");
                        return;
                    }
                }

                // Waves first: the drop sequence takes a reference to it, and on a first run
                // there would be nothing to take.
                if (!WireWaves(group, byProp, presenceByProp)) return;
                if (!WireSequence(group, boxes, lines)) return;

                BuildTrack(timeline, director, animator, scenery, props);

                EditorUtility.SetDirty(timeline);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(k_Timeline, ImportAssetOptions.ForceSynchronousImport);

                PrefabUtility.SaveAsPrefabAsset(root, k_Prefab);

                Debug.Log($"[O1Timeline] {k_Timeline}: {scenery.Count} box group(s) dissolve in " +
                          $"{k_InAt}-{k_InAt + k_In}s, then {props.Count} lamp(s) at " +
                          $"{k_PropAt}-{k_PropAt + k_PropIn}s, and both HELD to the end of a " +
                          $"{k_BeatSeconds}s beat; the show holds at {outAt}s, and what plays " +
                          "once both players are finished is the room as they left it -- O-2 " +
                          "dissolves the boxes out over its first scan forming.\n" +
                          $"[O1Timeline] {lines.Count} line(s) queued one per object: " +
                          $"{string.Join(", ", lines.ConvertAll(c => c.name))}.\n" +
                          "[O1Timeline] RESULT: PASS");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ------------------------------------------------------------------ audio

        static List<AudioClip> LoadLines(string[] paths)
        {
            var clips = new List<AudioClip>();
            foreach (string path in paths)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null)
                {
                    Debug.LogError($"[O1Timeline] RESULT: FAIL - no audio clip at {path}.");
                    return null;
                }
                clips.Add(clip);
            }
            return clips;
        }

        // ------------------------------------------------------------------ materials

        /// <summary>
        /// The carton, kept opaque and given something to clip against.
        ///
        /// Opaque is a decision BuildO1Box made and explains at length; this does not undo it. It
        /// only adds the alpha channel that lets an opaque thing leave in pieces instead of
        /// switching off.
        /// </summary>
        static Material BuildCartonMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(k_CartonMat);
            if (material == null)
            {
                Debug.LogError($"[O1Timeline] RESULT: FAIL - no material at {k_CartonMat}. Run " +
                               "Tools/O-1/Build the Drop Boxes first.");
                return null;
            }

            Texture2D map = BuildDissolveMap(k_CartonBase, k_CartonDissolve);
            if (map == null) return null;

            MakeClippable(material, map);
            Debug.Log($"[O1Timeline] {k_CartonMat} clips against {map.name}.");
            return material;
        }

        /// <summary>
        /// The wall of boxes gets a material of its own rather than the model's, because the
        /// model's own material is shared with whatever else that FBX is used for and this one
        /// has a cutoff being written to it every frame.
        /// </summary>
        static Material BuildShowcaseMaterial()
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(k_ShowcaseSourceMat);
            if (source == null)
            {
                Debug.LogError($"[O1Timeline] RESULT: FAIL - no material at {k_ShowcaseSourceMat}.");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(k_ShowcaseMat);
            if (material == null)
            {
                material = new Material(source.shader) { name = "O1ShowcaseBoxes" };
                AssetDatabase.CreateAsset(material, k_ShowcaseMat);
            }

            material.shader = source.shader;
            material.CopyPropertiesFromMaterial(source);
            material.shaderKeywords = source.shaderKeywords;

            Texture2D map = BuildDissolveMap(k_ShowcaseBase, k_ShowcaseDissolve);
            if (map == null) return null;

            MakeClippable(material, map);
            Debug.Log($"[O1Timeline] {k_ShowcaseMat} rebuilt from {source.name}, clipping " +
                      $"against {map.name}.");
            return material;
        }

        /// <summary>
        /// Opaque, and clipped against its base map's alpha. PropPresence writes _Cutoff.
        ///
        /// _BaseColor's alpha is forced to 1 because URP multiplies the two before clipping: a
        /// material whose tint happened to be half transparent would be clipped away entirely at
        /// a cutoff of a half, whatever the noise said.
        /// </summary>
        static void MakeClippable(Material material, Texture2D map)
        {
            material.SetTexture("_BaseMap", map);

            Color tint = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white;
            tint.a = 1f;
            material.SetColor("_BaseColor", tint);

            material.SetFloat("_Surface", 0f);      // 0 opaque, 1 transparent
            material.SetFloat("_AlphaClip", 1f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetFloat("_Cutoff", 0f);       // nothing clipped until something says so
            material.SetFloat("_SrcBlend", (float)BlendMode.One);
            material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.SetFloat("_ZWrite", 1f);
            material.SetShaderPassEnabled("ShadowCaster", true);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_SURFACE_TYPE_OPAQUE");
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;

            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// Turn one of the grabbable objects' materials into a clipped one, against a dissolve
        /// map generated from its own base texture.
        ///
        /// SAME TREATMENT AS THE CARTONS, and for a reason the room proved by itself: a lamp
        /// dragged into the scene straight from SmallProps is not hidden by the water, and the
        /// built one is. The only difference between them was that this one had been made
        /// alpha-blended so PropPresence could fade it. KWS's passes work off the depth buffer
        /// and _CameraOpaqueTexture, and a blended material writes neither -- so from the water
        /// system's point of view the object is not in the scene at all.
        ///
        /// Clipping costs nothing here: the objects dissolve rather than fade, which is what
        /// everything else in this beat does anyway, and they go back to writing depth and
        /// casting shadows like the solid things they are meant to be.
        /// </summary>
        static bool MakePropClippable(string materialPath)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                Debug.LogError($"[O1Timeline] RESULT: FAIL - no material at {materialPath}. Run " +
                               "Tools/O-1/Build Grab Interaction first.");
                return false;
            }

            Texture baseMap = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
            string source = baseMap != null ? AssetDatabase.GetAssetPath(baseMap) : null;

            if (string.IsNullOrEmpty(source) || !source.EndsWith(".png"))
            {
                Debug.LogError($"[O1Timeline] RESULT: FAIL - {material.name}'s base map is " +
                               $"{(baseMap == null ? "missing" : "not a PNG (" + source + ")")}, " +
                               "so there is nothing to write dissolve noise into. The map is " +
                               "decoded from disk; see BuildDissolveMap.");
                return false;
            }

            // Next to the source, as the cartons' are. Shared by the two roles' materials --
            // same mesh, same base map, so the same pattern, and BuildDissolveMap leaves an
            // existing one alone rather than re-rolling it.
            string dissolve = source.Substring(0, source.Length - 4) + "_Dissolve.png";

            Texture2D map = BuildDissolveMap(source, dissolve);
            if (map == null) return false;

            MakeClippable(material, map);
            Debug.Log($"[O1Timeline] {material.name} is opaque and clipped against {map.name}; " +
                      "PropPresence writes its cutoff.");
            return true;
        }

        /// <summary>
        /// A copy of a base map with value noise written into its alpha channel, which is what
        /// the cutoff clips against.
        ///
        /// THE PNG IS DECODED FROM DISK rather than read off the imported texture. Both sources
        /// import as DXT1, which has no alpha at all and is lossy in the three channels it does
        /// have -- reading pixels back would mean making the texture readable, uncompressing it,
        /// and copying block artefacts into the result.
        ///
        /// Built once. A re-run leaves an existing map alone, so re-running this builder does not
        /// re-roll the pattern -- which would silently change how the boxes come apart.
        /// </summary>
        static Texture2D BuildDissolveMap(string sourcePath, string outPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
            if (existing != null) return existing;

            if (!File.Exists(sourcePath))
            {
                Debug.LogError($"[O1Timeline] RESULT: FAIL - no base map at {sourcePath}.");
                return null;
            }

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!source.LoadImage(File.ReadAllBytes(sourcePath)))
            {
                Object.DestroyImmediate(source);
                Debug.LogError($"[O1Timeline] RESULT: FAIL - could not decode {sourcePath}.");
                return null;
            }

            int size = Mathf.Min(k_MapSize, Mathf.Max(source.width, source.height));
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    Color colour = source.GetPixelBilinear(u, v);

                    // 0.02 rather than 0, so every texel still survives a cutoff of nothing --
                    // clip() discards what is below zero, and a texel whose alpha was exactly 0
                    // would be gone from the first frame the box is fully present.
                    float noise = Mathf.Lerp(0.02f, 1f, Noise(u, v));

                    pixels[y * size + x] = new Color32(
                        (byte)(Mathf.Clamp01(colour.r) * 255f),
                        (byte)(Mathf.Clamp01(colour.g) * 255f),
                        (byte)(Mathf.Clamp01(colour.b) * 255f),
                        (byte)(noise * 255f));
                }
            }

            Object.DestroyImmediate(source);

            var map = new Texture2D(size, size, TextureFormat.RGBA32, false);
            map.SetPixels32(pixels);
            map.Apply();

            File.WriteAllBytes(outPath, map.EncodeToPNG());
            Object.DestroyImmediate(map);

            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceSynchronousImport);

            var importer = AssetImporter.GetAtPath(outPath) as TextureImporter;
            if (importer != null)
            {
                importer.alphaSource = TextureImporterAlphaSource.FromInput;

                // FALSE, and this is the one setting that would ruin it quietly. "Alpha is
                // transparency" dilates the colour channels outward from wherever alpha is low,
                // so Unity would smear the cardboard about according to the noise pattern and
                // the box would look wrong before anything had dissolved at all.
                importer.alphaIsTransparency = false;

                importer.sRGBTexture = true;
                importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }

            Debug.Log($"[O1Timeline] wrote {outPath} -- {size}x{size}, {sourcePath}'s colour with " +
                      $"{k_NoiseCells}-cell noise in its alpha.");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        }

        /// <summary>
        /// Smooth value noise, two octaves, seeded by nothing at all so the pattern is the same
        /// on every machine that builds this and a re-run cannot change how the boxes come apart.
        /// </summary>
        static float Noise(float u, float v)
        {
            float n = Octave(u, v, k_NoiseCells) * 0.65f +
                      Octave(u + 0.37f, v + 0.11f, k_NoiseCells * 3) * 0.35f;
            return Mathf.Clamp01(n);
        }

        static float Octave(float u, float v, int cells)
        {
            float x = u * cells, y = v * cells;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = Smooth(x - x0), fy = Smooth(y - y0);

            float a = Lattice(x0, y0), b = Lattice(x0 + 1, y0);
            float c = Lattice(x0, y0 + 1), d = Lattice(x0 + 1, y0 + 1);

            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        static float Lattice(int x, int y)
        {
            // An integer hash rather than Random.value: the pattern has to be identical on every
            // run and on every machine, and a seeded Random would still depend on how many times
            // anything else had drawn from it.
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
        }

        // ------------------------------------------------------------------ the wall of boxes

        /// <summary>
        /// Take the hand-staged wall of boxes out of MainScene and report where it was standing.
        ///
        /// It goes into the prefab instead, because a timeline binding is a reference to a scene
        /// object and the director that drives it lives in the prefab. Returns the identity
        /// matrix when there is nothing to take, which is the case on a second run -- the
        /// transform inside the prefab is then left exactly as it is.
        /// </summary>
        static bool TakeShowcaseFromScene(out Matrix4x4 staged)
        {
            staged = Matrix4x4.identity;

            Scene scene = EditorSceneManager.OpenScene(k_MainScene, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError($"[O1Timeline] RESULT: FAIL - could not open {k_MainScene}.");
                return false;
            }

            // The onboarding prefab's own instance, so the copy this builder put INSIDE it is not
            // mistaken for the loose one and deleted on every run.
            //
            // NOT "skip anything that is a prefab instance", which is what this said first and
            // what cost the wall its staging: the loose object is an instance of the FBX, so
            // GetCorrespondingObjectFromSource answers for it too, the builder decided there was
            // nothing to take, and then made a second one at the origin.
            Transform onboarding = null;
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            {
                foreach (Transform t in sceneRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) == k_Prefab)
                    {
                        onboarding = t;
                        break;
                    }
                }
                if (onboarding != null) break;
            }

            GameObject found = null;
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            {
                foreach (Transform t in sceneRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != k_ShowcaseName) continue;
                    if (onboarding != null && t.IsChildOf(onboarding)) continue;

                    found = t.gameObject;
                    break;
                }
                if (found != null) break;
            }

            if (found == null)
            {
                Debug.Log($"[O1Timeline] no loose '{k_ShowcaseName}' in {k_MainScene}; leaving " +
                          "the one in the prefab where it is.");
                return true;
            }

            Transform wall = found.transform;
            staged = Matrix4x4.TRS(wall.position, wall.rotation, wall.lossyScale);

            Debug.Log($"[O1Timeline] took '{found.name}' out of {FullPath(wall)} -- staged at " +
                      $"{wall.position.ToString("F2")}, {wall.eulerAngles.y:F1} deg, " +
                      $"scale {wall.lossyScale.x:F2}; it goes inside the prefab with the beat " +
                      "that owns it.");

            Object.DestroyImmediate(found);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return true;
        }

        /// <summary>
        /// The wall of boxes inside the prefab: the model, the clipping material, and a presence
        /// for the timeline to drive. A re-run leaves its transform alone -- where a thing stands
        /// is a hand decision.
        /// </summary>
        static Transform EnsureShowcase(Transform group, Matrix4x4 staged, Material material)
        {
            Transform wall = group.Find(k_ShowcaseName);

            if (wall == null)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(k_ShowcaseModel);
                if (model == null)
                {
                    Debug.LogError($"[O1Timeline] RESULT: FAIL - no model at {k_ShowcaseModel}.");
                    return null;
                }

                var made = (GameObject)PrefabUtility.InstantiatePrefab(model);
                made.name = k_ShowcaseName;
                made.transform.SetParent(group, false);
                wall = made.transform;

                Debug.Log($"[O1Timeline] put '{k_ShowcaseName}' under {k_GrabGroup}.");
            }

            // Only when something was just taken out of the scene. That is the hand staging
            // being carried across, and carrying it across is the whole point; on every later
            // run there is nothing to take and the transform inside the prefab is left alone,
            // because where a thing stands is a hand decision.
            if (staged != Matrix4x4.identity)
            {
                wall.SetPositionAndRotation(staged.GetColumn(3), staged.rotation);

                Vector3 parent = group.lossyScale;
                Vector3 target = staged.lossyScale;
                wall.localScale = new Vector3(
                    Mathf.Approximately(parent.x, 0f) ? target.x : target.x / parent.x,
                    Mathf.Approximately(parent.y, 0f) ? target.y : target.y / parent.y,
                    Mathf.Approximately(parent.z, 0f) ? target.z : target.z / parent.z);

                Debug.Log($"[O1Timeline] '{k_ShowcaseName}' kept its staging: " +
                          $"{wall.localPosition.ToString("F2")}, {wall.localEulerAngles.y:F1} deg, " +
                          $"scale {wall.localScale.x:F2}.");
            }
            else if (wall.localScale == Vector3.one && wall.localPosition == Vector3.zero)
            {
                Debug.LogWarning($"[O1Timeline] '{k_ShowcaseName}' is at the origin at scale 1 " +
                                 "and there was nothing in the scene to take its staging from. " +
                                 "Put it where it belongs in MainScene and it will stay there.");
            }

            var renderers = new List<Renderer>(wall.GetComponentsInChildren<Renderer>(true));
            foreach (Renderer r in renderers) r.sharedMaterial = material;

            // No collider on any of them: this is scenery at the far end of the room, and a
            // player who threw a coat at it should not have it bounce off.
            foreach (Collider collider in wall.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider, true);

            EnsurePresence(wall.gameObject, PropPresence.Mode.Clip, renderers,
                           new List<XRGrabInteractable>());

            return wall;
        }

        // ------------------------------------------------------------------ the two stations

        /// <summary>
        /// One player's object and their carton: two presences, because the two are different
        /// kinds of material. The object is transparent and fades; the carton is opaque and
        /// clips.
        /// </summary>
        static bool WireStation(Transform group, Transform station, PlayerRole role,
                                Material carton, List<string> scenery, List<string> props,
                                List<(PlayerRole, O1DropBox)> boxes)
        {
            // THE CARTON IS THE PLAYER'S, NOT THE OBJECT'S, so most stations do not have one.
            // 「兩人面前各出現一個中型紙箱」 -- one box each, and every object that player is shown
            // goes into it; BuildO1Box hangs it under the FIRST object's station because that
            // station's RoleVisibility already hides it from the other player. A station
            // without one is the normal case now, not a build that was run in the wrong order.
            Transform box = station.Find("Box");

            // The prop's own renderers, which is everything under the station EXCEPT the carton.
            // Four of them now rather than one: the lamp is a base, a stem, a collar and a shade.
            var lamp = new List<Renderer>();
            foreach (Renderer r in station.GetComponentsInChildren<Renderer>(true))
            {
                if (box == null || !r.transform.IsChildOf(box)) lamp.Add(r);
            }

            var grabs = new List<XRGrabInteractable>(
                station.GetComponentsInChildren<XRGrabInteractable>(true));

            // CLIP, NOT FADE. Fade means an alpha-blended material, and an alpha-blended
            // material is invisible to the water system's passes -- which is what put these
            // objects behind the water. See MakePropClippable.
            EnsurePresence(station.gameObject, PropPresence.Mode.Clip, lamp, grabs);

            // null means "this object is not the timeline's". Only the FIRST object arrives at
            // an authored time; the rest are brought in by O1GrabWaves when the previous one's
            // line finishes, and a curve on the track would overwrite the component on every
            // frame the director is evaluated.
            props?.Add(Path(group, station));

            if (box != null)
            {
                var cartons = new List<Renderer>(box.GetComponentsInChildren<Renderer>(true));
                foreach (Renderer r in cartons) r.sharedMaterial = carton;

                EnsurePresence(box.gameObject, PropPresence.Mode.Clip, cartons,
                               new List<XRGrabInteractable>());
                scenery.Add(Path(group, box));

                // ONE WATCHER PER OBJECT, UNDER THE BOX. There is one carton and several things
                // to put in it, so the drop targets are children of it -- see BuildO1Box.
                //
                // AND THE GATE IS NONE OF THEIRS, nor is the fallback: O1DropSequence waits for
                // every one of them. A box left reporting the beat's task would clear the hold
                // on the first drop, with the other objects still in the room.
                var drops = new List<O1DropBox>(box.GetComponentsInChildren<O1DropBox>(true));
                if (drops.Count == 0)
                {
                    Debug.LogError($"[O1Timeline] RESULT: FAIL - '{station.name}/Box' has no " +
                                   "O1DropBox under it. Run Tools/O-1/Build the Drop Boxes " +
                                   "first.");
                    return false;
                }

                foreach (O1DropBox drop in drops)
                {
                    var dso = new SerializedObject(drop);
                    dso.FindProperty("m_TaskId").stringValue = "";
                    dso.FindProperty("m_Timeout").floatValue = 0f;
                    dso.ApplyModifiedPropertiesWithoutUndo();

                    boxes.Add((role, drop));
                }
            }

            // AND THE GRAB NO LONGER SPEAKS. S0-1-A and S0-1-B played when the object was picked
            // up, which with the new audio would be a line on the grab and another on the drop
            // for the same object. 「每當玩家把一個物件放進箱子後會按順序撥放音檔」 -- the drop is where
            // the speech belongs, so the grab keeps its own row in the debug panel and is silent.
            var grab = station.GetComponent<O1GrabStation>();
            if (grab != null)
            {
                var gso = new SerializedObject(grab);
                SerializedProperty cue = gso.FindProperty("m_Cue");
                if (cue.objectReferenceValue != null)
                {
                    Debug.Log($"[O1Timeline] cleared '{station.name}'s grab cue " +
                              $"({cue.objectReferenceValue.name}); the sequence speaks on the " +
                              "drop now.");
                    cue.objectReferenceValue = null;
                    gso.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            return true;
        }

        static void EnsurePresence(GameObject target, PropPresence.Mode mode,
                                   List<Renderer> renderers, List<XRGrabInteractable> grabs)
        {
            var presence = target.GetComponent<PropPresence>();
            if (presence == null) presence = target.AddComponent<PropPresence>();

            var so = new SerializedObject(presence);

            // ZERO IN THE PREFAB. An animation track only drives its target while its director
            // is being evaluated, and this director is only driven during O-1 -- so before the
            // beat begins nothing is hiding these, and whatever the prefab says is what the room
            // shows. Absent is therefore the authored state, exactly as O-2's orbits are off.
            so.FindProperty("m_Presence").floatValue = 0f;
            so.FindProperty("m_Mode").enumValueIndex = (int)mode;

            SerializedProperty list = so.FindProperty("m_Renderers");
            list.arraySize = renderers.Count;
            for (int i = 0; i < renderers.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];

            SerializedProperty held = so.FindProperty("m_Grabs");
            held.arraySize = grabs.Count;
            for (int i = 0; i < grabs.Count; i++)
                held.GetArrayElementAtIndex(i).objectReferenceValue = grabs[i];

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ the sequence

        /// <summary>
        /// The chain of objects: both players grab one, a line answers them, and the next one
        /// arrives when it finishes.
        ///
        /// ONE WAVE PER LINE. <see cref="k_WaveLines"/> is the running order, so a second clip
        /// there is a second object -- and the wave it makes will have no stations and nothing
        /// to fade in until a second pair of stations exists, which the log says plainly rather
        /// than leaving a silent hole in the beat.
        ///
        /// THE FIRST WAVE BRINGS IN NOTHING. Its lamps are on the animation track above, at a
        /// time somebody chose; only the waves after it are faded in by the component, because
        /// when they arrive depends on when each player put the last one away. That is also why
        /// a later wave's objects must be kept OFF the track: the track would overwrite the
        /// component on every frame the director is evaluated.
        ///
        /// A WAVE IS WIRED PER PLAYER, NOT PER OBJECT: each side carries its own station and
        /// its own presence together, so the two are always the same player's. Parallel arrays
        /// would let the jacket arrive for whoever happened to be at the same index.
        ///
        /// IT NO LONGER CARRIES A DROP BOX. 「兩個人都抓完燈後 vo跑完 外套才會出現」 -- what brings
        /// the next object in is the line finishing, for both players at once, so the boxes
        /// drive nothing in the chain. They are still the beat's gate, through O1DropSequence.
        /// </summary>
        static bool WireWaves(Transform group, List<List<O1GrabStation>> byProp,
                              List<List<PropPresence>> presenceByProp)
        {
            var waves = group.GetComponent<O1GrabWaves>();
            if (waves == null) waves = group.gameObject.AddComponent<O1GrabWaves>();

            // ITS OWN SPEAKER, NOT THE DROP SEQUENCE'S. Both are 2D and both speak for the room,
            // but one queue holding two unrelated kinds of line would mean a drop could delay a
            // line the whole beat is waiting on -- and 「播完後下一個物件才會出現」 makes that line
            // the thing the next object is timed from.
            Transform speaker = group.Find(k_WaveSpeakerName);
            if (speaker == null)
            {
                var made = new GameObject(k_WaveSpeakerName);
                made.transform.SetParent(group, false);
                speaker = made.transform;
            }

            var source = speaker.GetComponent<AudioSource>();
            if (source == null) source = speaker.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;    // the room, not a thing one player stands nearer to

            var so = new SerializedObject(waves);
            SerializedProperty list = so.FindProperty("m_Waves");
            list.arraySize = k_WaveLines.Length;

            var summary = new List<string>();

            for (int i = 0; i < k_WaveLines.Length; i++)
            {
                (string name, string shared, string a, string b, bool pause) spec = k_WaveLines[i];

                SerializedProperty wave = list.GetArrayElementAtIndex(i);
                wave.FindPropertyRelative("name").stringValue = spec.name;

                // Unique per wave, or the second wave unlocks on the first wave's grabs. The
                // number and not the wave's name: a task id crosses the network as a
                // FixedString64Bytes, and these are named in Chinese.
                wave.FindPropertyRelative("taskId").stringValue = $"o1.hold.{i + 1}";

                if (!Lines(wave, spec, out float longest)) return false;

                SerializedProperty sides = wave.FindPropertyRelative("sides");
                sides.arraySize = 0;

                // A PAUSE IS LEFT EMPTY ON PURPOSE. 「這裡先不要加互動」 -- no sides at all is what
                // makes O1GrabWaves speak it by itself once every line before it has been
                // heard, and the beat's hold waits for it like any other line.
                if (spec.pause)
                {
                    summary.Add($"{i + 1}. {spec.name} ({longest:F1}s, THE PAUSE -- speaks by " +
                                "itself, no interaction)");
                    continue;
                }

                if (i >= byProp.Count)
                {
                    summary.Add($"{i + 1}. {spec.name} ({longest:F1}s, NO OBJECTS)");

                    Debug.LogWarning($"[O1Timeline] wave {i + 1} '{spec.name}' is a line and " +
                                     "nothing else: BuildO1Grab.Props has no object for it, so " +
                                     "there is nothing to grab and nothing to fade in. The " +
                                     $"chain stops after wave {i} and the show never comes off " +
                                     "O-1's hold except by its fallback. Mark it as the pause " +
                                     "if that is what it is.");
                    continue;
                }

                List<O1GrabStation> stations = byProp[i];
                List<PropPresence> presences = presenceByProp[i];

                sides.arraySize = stations.Count;
                for (int s = 0; s < stations.Count; s++)
                {
                    SerializedProperty side = sides.GetArrayElementAtIndex(s);
                    side.FindPropertyRelative("role").enumValueIndex = (int)stations[s].owner;
                    side.FindPropertyRelative("station").objectReferenceValue = stations[s];

                    // THE FIRST OBJECT HAS NO PRESENCE HERE, and that is the one-owner rule:
                    // the lamps are on the animation track at a time somebody chose, and a
                    // component writing the same float would be overwritten on every frame the
                    // director is evaluated. Every later object is this component's, faded in
                    // when ITS OWN player fills their box.
                    side.FindPropertyRelative("appears").objectReferenceValue =
                        i > 0 ? presences[s] : null;
                }

                summary.Add($"{i + 1}. {spec.name} ({longest:F1}s, {stations.Count} player(s), " +
                            $"{(i == 0 ? "arrives on the track" : "arrives on the line before it")})");
            }

            so.FindProperty("m_Source").objectReferenceValue = source;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[O1Timeline] O1GrabWaves on {group.name}: {k_WaveLines.Length} wave(s) " +
                      $"-- {string.Join("; ", summary)}. A line waits for BOTH players to be " +
                      "holding that object ('o1.hold.N'), and the objects that follow it " +
                      "arrive for both of them when it ends. The last line to finish is what " +
                      "lets the show off O-1's hold and into O-2.");

            if (BuildO1Grab.Props.Length > k_WaveLines.Length)
                Debug.LogWarning($"[O1Timeline] BuildO1Grab.Props has " +
                                 $"{BuildO1Grab.Props.Length} object(s) and the running order " +
                                 $"has {k_WaveLines.Length} line(s); the extra objects are " +
                                 "built and hidden but nothing ever brings them in.");
            return true;
        }

        /// <summary>
        /// A wave's speech: one shared recording, or one per role. Returns false if a clip named
        /// in the running order is missing, and gives back the longest of them -- which is what
        /// the chain is timed off, so both machines advance together.
        /// </summary>
        static bool Lines(SerializedProperty wave,
                          (string name, string shared, string a, string b, bool pause) spec,
                          out float longest)
        {
            longest = 0f;

            SerializedProperty shared = wave.FindPropertyRelative("line");
            SerializedProperty own = wave.FindPropertyRelative("ownLines");

            shared.objectReferenceValue = null;
            own.arraySize = 0;

            if (!string.IsNullOrEmpty(spec.shared))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(spec.shared);
                if (clip == null)
                {
                    Debug.LogError($"[O1Timeline] RESULT: FAIL - no audio clip at {spec.shared}.");
                    return false;
                }
                shared.objectReferenceValue = clip;
                longest = clip.length;
                return true;
            }

            var roles = new[] { (PlayerRole.Player1, spec.a), (PlayerRole.Player2, spec.b) };
            own.arraySize = roles.Length;

            for (int i = 0; i < roles.Length; i++)
            {
                (PlayerRole role, string path) = roles[i];
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null)
                {
                    Debug.LogError($"[O1Timeline] RESULT: FAIL - no audio clip at {path} for " +
                                   $"{role}'s half of '{spec.name}'.");
                    return false;
                }

                SerializedProperty entry = own.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("role").enumValueIndex = (int)role;
                entry.FindPropertyRelative("clip").objectReferenceValue = clip;
                longest = Mathf.Max(longest, clip.length);
            }

            return true;
        }

        static bool WireSequence(Transform group, List<(PlayerRole role, O1DropBox box)> boxes,
                                 List<AudioClip> lines)
        {
            var sequence = group.GetComponent<O1DropSequence>();
            if (sequence == null) sequence = group.gameObject.AddComponent<O1DropSequence>();

            AudioSource speaker = EnsureSpeaker(group);

            var so = new SerializedObject(sequence);
            SerializedProperty sides = so.FindProperty("m_Sides");

            var roles = new[] { PlayerRole.Player1, PlayerRole.Player2 };
            sides.arraySize = roles.Length;

            for (int i = 0; i < roles.Length; i++)
            {
                SerializedProperty side = sides.GetArrayElementAtIndex(i);
                side.FindPropertyRelative("role").enumValueIndex = (int)roles[i];

                var mine = new List<O1DropBox>();
                foreach ((PlayerRole role, O1DropBox box) in boxes)
                {
                    if (role == roles[i]) mine.Add(box);
                }

                SerializedProperty list = side.FindPropertyRelative("boxes");
                list.arraySize = mine.Count;
                for (int b = 0; b < mine.Count; b++)
                    list.GetArrayElementAtIndex(b).objectReferenceValue = mine[b];

                // One line per object, in the order they are listed. A shorter list simply runs
                // out, which is the right failure: the later drops are silent rather than
                // repeating something that has already been said.
                SerializedProperty clips = side.FindPropertyRelative("clips");
                int spoken = Mathf.Min(lines.Count, mine.Count);
                clips.arraySize = spoken;
                for (int c = 0; c < spoken; c++)
                    clips.GetArrayElementAtIndex(c).objectReferenceValue = lines[c];

                // Only when a list was STARTED and ran out. An empty list is the beat as it is
                // written -- the speech answers the grab now, not the drop -- and a warning for
                // that would fire on every run and mean nothing.
                if (lines.Count > 0 && mine.Count > lines.Count)
                    Debug.LogWarning($"[O1Timeline] {roles[i]} has {mine.Count} box(es) and only " +
                                     $"{lines.Count} line(s); the later drops will be silent.");
            }

            so.FindProperty("m_Source").objectReferenceValue = speaker;
            so.FindProperty("m_TaskId").stringValue = "beat.O1_Control";

            // Wired here rather than found at runtime: the gate must not clear while the beat's
            // shared line is still sounding, and a reference that is only resolved if it happens
            // to be on the same object is the kind that goes missing without anyone noticing.
            so.FindProperty("m_Waves").objectReferenceValue = group.GetComponent<O1GrabWaves>();
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[O1Timeline] O1DropSequence on {group.name}: " +
                      $"{boxes.Count} box(es) across {roles.Length} side(s), reporting " +
                      "'beat.O1_Control' once both are finished and the speaker is quiet.");
            return true;
        }

        /// <summary>
        /// The speaker for the lines, inside the prefab so it travels with the beat. 2D: these
        /// are the room speaking rather than a thing in it, which is also what keeps a line
        /// audible while the player is bent over a box with their head next to it.
        /// </summary>
        static AudioSource EnsureSpeaker(Transform group)
        {
            Transform speaker = group.Find(k_SpeakerName);
            if (speaker == null)
            {
                var go = new GameObject(k_SpeakerName);
                go.transform.SetParent(group, false);
                speaker = go.transform;
                Debug.Log($"[O1Timeline] created '{k_SpeakerName}' in the prefab.");
            }

            var source = speaker.GetComponent<AudioSource>();
            if (source == null) source = speaker.gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            return source;
        }

        // ------------------------------------------------------------------ the timeline

        static TimelineAsset LoadOrCreate()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_Timeline);
            if (existing != null) return existing;

            var created = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(created, k_Timeline);
            Debug.Log($"[O1Timeline] created {k_Timeline}.");
            return AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_Timeline);
        }

        /// <summary>
        /// Emptied and rebuilt rather than replaced, so the asset keeps its GUID and the
        /// director pointing at it survives. The clip it holds goes too -- a detached animation
        /// clip stays in the file as an invisible orphan otherwise.
        /// </summary>
        static void Clear(TimelineAsset timeline)
        {
            foreach (TrackAsset track in new List<TrackAsset>(timeline.GetRootTracks()))
                timeline.DeleteTrack(track);

            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(k_Timeline))
            {
                if (sub is AnimationClip orphan) Object.DestroyImmediate(orphan, true);
            }
        }

        static PlayableDirector EnsureDirector(Transform root, TimelineAsset timeline)
        {
            Transform holder = root.Find(DirectorName);
            if (holder == null)
            {
                var go = new GameObject(DirectorName);
                go.transform.SetParent(root, false);
                holder = go.transform;
                Debug.Log($"[O1Timeline] created '{DirectorName}' in the prefab.");
            }

            var director = holder.GetComponent<PlayableDirector>();
            if (director == null) director = holder.gameObject.AddComponent<PlayableDirector>();

            director.playableAsset = timeline;

            // Driven by the master's Control clip, never by itself.
            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.Manual;
            director.extrapolationMode = DirectorWrapMode.Hold;

            return director;
        }

        /// <summary>
        /// An Animator on the group the curves are written against. It has no controller and
        /// animates nothing on its own; an animation track simply has to bind to one.
        /// </summary>
        static Animator EnsureAnimator(Transform group)
        {
            var animator = group.GetComponent<Animator>();
            if (animator == null) animator = group.gameObject.AddComponent<Animator>();

            // The props are at the far end of a dark room and the wall of boxes is behind them,
            // so a renderer that happens to be off screen must not stop the dissolve: a group
            // that was culled would come back half present.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return animator;
        }

        /// <summary>
        /// One clip across the whole beat, one curve per group: absent, in, held, out, absent.
        ///
        /// TWO ARRIVALS AND ONE DEPARTURE. The wall and the cartons come in on the first pair of
        /// numbers, the lamps on the second and later, because 「出現後 燈會出現」 -- the room is
        /// furnished and then the thing to be put away appears over the box. They all leave
        /// together: the dissolve-out is the tail of the beat and there is nothing to stagger
        /// about an empty room.
        ///
        /// FIVE KEYS AND NOT FOUR. The first is at zero rather than at the start of the fade,
        /// because a curve whose first key is a second and a half in is flat BEFORE it -- at the
        /// value of that first key, which is zero here, so it happens to be right. It is written
        /// down anyway: if the beat ever starts with the boxes already present, the difference
        /// between those two readings is the whole bug.
        /// </summary>
        static void BuildTrack(TimelineAsset timeline, PlayableDirector director,
                               Animator animator, List<string> scenery, List<string> props)
        {
            var clip = new AnimationClip { name = k_ClipName, frameRate = 60f };

            Curve(clip, scenery, k_InAt, k_In);
            Curve(clip, props, k_PropAt, k_PropIn);

            AssetDatabase.AddObjectToAsset(clip, timeline);
            EditorUtility.SetDirty(clip);

            var track = timeline.CreateTrack<AnimationTrack>(null, k_TrackName);
            TimelineClip timelineClip = track.CreateClip(clip);
            timelineClip.start = 0d;
            timelineClip.duration = k_BeatSeconds;
            timelineClip.displayName = "Boxes and the wall, then the lamps";

            director.SetGenericBinding(track, animator);

            Debug.Log($"[O1Timeline] track '{k_TrackName}' over {k_BeatSeconds}s:\n" +
                      $"  {k_InAt}-{k_InAt + k_In}s  {string.Join(", ", scenery)}\n" +
                      $"  {k_PropAt}-{k_PropAt + k_PropIn}s  {string.Join(", ", props)}\n" +
                      "  and then nothing leaves: O-2 takes them away, not this.");
        }

        /// <summary>
        /// One group's arrival, written against every path in it. It arrives and then stays.
        ///
        /// NOTHING LEAVES ON THIS TIMELINE ANY MORE. 「到o-2 箱子(box &amp; Showcase_CB_boxes)要fade
        /// out, scan人物都要fade in」 -- the room is emptied by the beat that fills it, so the
        /// cartons, the wall and the objects hold at 1 to the end of O-1 and
        /// <see cref="BuildO2Timeline"/> takes them away over the top of its first scan
        /// forming. A dissolve that finished before O-2 began would have left the players
        /// looking at an empty room waiting for a ghost; crossing the two makes it an exchange.
        ///
        /// WHICH LEAVES O-1'S LAST <see cref="k_OutLead"/> SECONDS WITH NOTHING TO SHOW. They
        /// are no longer a dissolve, just the room as the two of them left it, after the last
        /// line and before O-2. That is a breath rather than a hole -- but if it reads as dead
        /// air, the number to change is k_OutLead, which also moves the master's hold marker.
        /// </summary>
        static void Curve(AnimationClip clip, List<string> paths, double at, double over)
        {
            foreach (string path in paths)
            {
                clip.SetCurve(path, typeof(PropPresence), "m_Presence", new AnimationCurve(
                    new Keyframe(0f, 0f, 0f, 0f),
                    new Keyframe((float)at, 0f, 0f, 0f),
                    new Keyframe((float)(at + over), 1f, 0f, 0f),
                    new Keyframe((float)k_BeatSeconds, 1f, 0f, 0f)));
            }
        }

        // ------------------------------------------------------------------ helpers

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

        /// <summary>An animation curve path: the target's path relative to the Animator's object.</summary>
        static string Path(Transform animatorOn, Transform target)
        {
            var parts = new List<string>();
            for (Transform t = target; t != null && t != animatorOn; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        static string FullPath(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
