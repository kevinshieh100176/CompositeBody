using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using CompositeBody.Multiplayer;
using CompositeBody.PointClouds;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// S1-4's 真人 on the left, split per player.
    ///
    /// 「在main timeline 上s1-4的時候現在 左手邊有一個點雲scan figure_a5 我想要玩家a繼續看到a5 但玩家b
    /// 看到 figure_b5（我已經放好位置了）幫我套上跟a5一模一樣的那些進出場效果還有point cloud figure
    /// script參數」
    ///
    /// Figure_B5 was already in the scene beside Figure_A5, placed by hand, with its own .ply and
    /// nothing driving it. So three things happen here:
    ///
    /// <list type="bullet">
    /// <item>Figure_B5 gets a <see cref="PointCloudFigure"/> whose every field is COPIED off
    /// Figure_A5's, so the two bodies shimmer, glitch and come apart identically.</item>
    /// <item>It gets a <see cref="FigurePresenceTrack"/> on Main that is a copy of Figure_A5's --
    /// same clip, same start, same duration, same curve -- inside the same 「S1 點雲進出」 group,
    /// so both players' figure arrives and leaves on the same second.</item>
    /// <item>Both figures get a <see cref="FigureRoleReveal"/>, Figure_A5 for 玩家A and
    /// Figure_B5 for 玩家B, which is the whole of the asymmetry.</item>
    /// </list>
    ///
    /// THE CUE IS COPIED, NOT RE-DERIVED, and that is the point of this tool rather than a
    /// re-typed pair of numbers. 「一模一樣」 means the curve too: Figure_A5's clip is four keys with
    /// flat tangents -- 4 s to gather, a hold, 4 s to go -- authored by hand in the Timeline
    /// window, and the 「S1 點雲進出」 group has no builder that owns it. Reading the shape off the
    /// track that is already there is the only way the copy stays a copy when somebody reshapes
    /// the original.
    ///
    /// SO THIS IS NOT A BUILDER. It does not own that group and must never clear it: it touches
    /// Figure_A5's track only to rename it, and the only track it rebuilds is its own. Re-running
    /// it after a re-time of Figure_A5 is how Figure_B5 catches up.
    ///
    /// Unity.exe -batchmode -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.SplitS1Figure5.Run
    /// </summary>
    public static class SplitS1Figure5
    {
        const string k_MainScene = "Assets/_Scenes/MainScene.unity";
        const string k_TimelineAsset = "Assets/_Timeline/Main.playable";

        const string k_S13 = "Experience/Beats/Beat_S1/Content/s1_v4/S1-3";

        /// <summary>
        /// The track the cue is read off, and the name it is given once it is one player's.
        ///
        /// MATCHED ON THE PREFIX, because this tool renames it: a second run has to find
        /// 'Figure_A5（玩家A看見）' as readily as the 'Figure_A5' it started as, or it would build a
        /// third track beside the two it made and the show would run one cue twice.
        /// </summary>
        const string k_ATrackPrefix = "Figure_A5";
        const string k_ATrack = "Figure_A5（玩家A看見）";
        const string k_BTrack = "Figure_B5（玩家B看見）";

        [MenuItem("Tools/S-1/Split S1-4 真人 A5／B5 per player")]
        public static void Run()
        {
            Debug.Log("[S1Figure5] Starting...");

            // THIS TOOL DOES NOT OPEN THE SCENE, for the reason BuildS3Figures gives: every other
            // tool here opens MainScene Single and would throw away whatever is in front of
            // whoever ran it, and this one saves at the end, so it must not be allowed to commit
            // someone else's half-finished work either.
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != k_MainScene)
            {
                Debug.LogError($"[S1Figure5] RESULT: FAIL - {k_MainScene} is not the open scene " +
                               $"('{scene.path}' is). Open it and run this again.");
                return;
            }

            if (scene.isDirty)
            {
                Debug.LogError("[S1Figure5] RESULT: FAIL - MainScene has unsaved changes and this " +
                               "tool saves the scene. Save or revert first.");
                return;
            }

            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_TimelineAsset);
            if (timeline == null)
            {
                Debug.LogError($"[S1Figure5] RESULT: FAIL - no timeline at {k_TimelineAsset}.");
                return;
            }

            PlayableDirector director = FindShowDirector(timeline);
            if (director == null)
            {
                Debug.LogError("[S1Figure5] RESULT: FAIL - nothing in MainScene plays " +
                               $"{k_TimelineAsset}, so there is no director to bind a track on.");
                return;
            }

            // --- the two bodies -------------------------------------------------------------
            GameObject aGo = Find($"{k_S13}/Figure_A5");
            GameObject bGo = Find($"{k_S13}/Figure_B5");
            if (aGo == null)
            {
                Debug.LogError($"[S1Figure5] RESULT: FAIL - no {k_S13}/Figure_A5 in MainScene.");
                return;
            }

            if (bGo == null)
            {
                Debug.LogError($"[S1Figure5] RESULT: FAIL - no {k_S13}/Figure_B5 in MainScene. It " +
                               "is the figure that was placed by hand; this tool does not create " +
                               "it.");
                return;
            }

            var aFigure = aGo.GetComponent<PointCloudFigure>();
            if (aFigure == null)
            {
                Debug.LogError("[S1Figure5] RESULT: FAIL - Figure_A5 has no PointCloudFigure, so " +
                               "there are no parameters to copy off it.");
                return;
            }

            // --- the track the cue is read off ----------------------------------------------
            FigurePresenceTrack aTrack = FindTrack(timeline, k_ATrackPrefix);
            if (aTrack == null)
            {
                Debug.LogError($"[S1Figure5] RESULT: FAIL - no FigurePresenceTrack whose name " +
                               $"starts with '{k_ATrackPrefix}' on Main, so Figure_A5's 進出 is " +
                               "not knowable. It is the one in the 「S1 點雲進出」 group.");
                return;
            }

            var bound = director.GetGenericBinding(aTrack) as PointCloudFigure;
            if (bound != aFigure)
            {
                Debug.LogError($"[S1Figure5] RESULT: FAIL - '{aTrack.name}' is bound to " +
                               $"{(bound == null ? "nothing" : bound.name)}, not to " +
                               $"{k_S13}/Figure_A5. Two Figure_A5s in the scene would make this " +
                               "copy the wrong cue.");
                return;
            }

            var group = aTrack.parent as TrackAsset;
            if (group == null)
                Debug.LogWarning($"[S1Figure5] '{aTrack.name}' is not inside a group track, so " +
                                 "Figure_B5's track goes at the root of Main beside it.");

            // --- Figure_B5's component, every field off Figure_A5's -------------------------
            PointCloudFigure bFigure = CopyFigure(aFigure, bGo);
            if (bFigure == null) return;

            // --- which player draws which --------------------------------------------------
            if (!Gate(aFigure, PlayerRole.Player1)) return;
            if (!Gate(bFigure, PlayerRole.Player2)) return;

            // --- and the cue, copied clip for clip -----------------------------------------
            FigurePresenceTrack bTrack = Rebuild(timeline, group, k_BTrack);
            bTrack.presenceBeforeFirstClip = aTrack.presenceBeforeFirstClip;

            int clips = 0;
            foreach (TimelineClip source in aTrack.GetClips())
            {
                if (source.asset is not FigurePresenceClip from) continue;

                TimelineClip copy = bTrack.CreateClip<FigurePresenceClip>();
                copy.start = source.start;
                copy.duration = source.duration;
                copy.displayName = source.displayName;

                var to = (FigurePresenceClip)copy.asset;
                to.name = from.name;

                // A NEW CURVE, NOT THE SAME ONE. AnimationCurve is a class: assigning the
                // original would leave the two clips sharing one object, so reshaping Figure_B5's
                // in the Timeline window would silently reshape Figure_A5's too.
                to.template.presence = new AnimationCurve(from.template.presence.keys)
                {
                    preWrapMode = from.template.presence.preWrapMode,
                    postWrapMode = from.template.presence.postWrapMode,
                };

                EditorUtility.SetDirty(to);
                clips++;

                Debug.Log($"[S1Figure5] '{k_BTrack}' <- '{source.displayName}': " +
                          $"{copy.start:F0}s for {copy.duration:F0}s, " +
                          $"{to.template.presence.keys.Length} keys.");
            }

            if (clips == 0)
            {
                Debug.LogError($"[S1Figure5] RESULT: FAIL - '{aTrack.name}' has no " +
                               "FigurePresenceClip on it, so there is no 進出 to copy.");
                return;
            }

            director.SetGenericBinding(bTrack, bFigure);

            // The original says whose it is now, the same way S3-4's two tracks do. Last, so a
            // failure above leaves the running order exactly as it was.
            if (aTrack.name != k_ATrack)
            {
                string was = aTrack.name;
                aTrack.name = k_ATrack;
                Debug.Log($"[S1Figure5] '{was}' renamed '{k_ATrack}' -- it is one player's cue now.");
            }

            EditorUtility.SetDirty(timeline);
            EditorUtility.SetDirty(director);
            AssetDatabase.SaveAssets();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"[S1Figure5] 玩家A draws {aGo.name}, 玩家B draws {bGo.name}; both on the " +
                      $"same {clips}-clip cue from '{aTrack.name}'.");
            Debug.Log("[S1Figure5] RESULT: PASS");
        }

        // ------------------------------------------------------------------- scene side

        /// <summary>
        /// Figure_A5's driver on Figure_B5, field for field.
        ///
        /// EVERY SERIALIZED FIELD, iterated rather than listed, so a parameter added to
        /// <see cref="PointCloudFigure"/> tomorrow is copied too -- 「一模一樣」 is the whole ask and
        /// a hand-written list of twenty floats is a list that goes stale.
        ///
        /// m_Dissolve IS COPIED WITH THE REST, and it is the one field that looks like a mistake.
        /// It is state, not a parameter: the presence track's mixer writes it every frame of the
        /// show from the authored curve, and answers 0 presence -- gone -- from frame 0, so the
        /// authored value never reaches a headset. What it does do is decide how the body looks in
        /// the scene view while somebody is placing it, and there the two figures matching is
        /// exactly what was asked for.
        ///
        /// m_Cloud IS NOT. It is a reference to Figure_A5's own renderer, and a copy of it would
        /// point Figure_B5's driver at Figure_A5's points: one body would wear both dissolves and
        /// the other would never move.
        /// </summary>
        static PointCloudFigure CopyFigure(PointCloudFigure from, GameObject onto)
        {
            var renderer = onto.GetComponent<Renderer>();
            if (renderer == null) renderer = onto.GetComponentInChildren<Renderer>();
            if (renderer == null)
            {
                Debug.LogError($"[S1Figure5] RESULT: FAIL - {onto.name} has no Renderer, so there " +
                               "is no cloud for a PointCloudFigure to drive.");
                return null;
            }

            var to = onto.GetComponent<PointCloudFigure>();
            if (to == null)
            {
                to = Undo.AddComponent<PointCloudFigure>(onto);
                Debug.Log($"[S1Figure5] {onto.name}: PointCloudFigure added.");
            }

            var source = new SerializedObject(from);
            var target = new SerializedObject(to);

            var copied = new List<string>();
            SerializedProperty p = source.GetIterator();
            if (p.NextVisible(true))
            {
                do
                {
                    if (p.propertyPath is "m_Script" or "m_Cloud") continue;
                    target.CopyFromSerializedProperty(p);
                    copied.Add(p.propertyPath);
                } while (p.NextVisible(false));
            }

            SerializedProperty cloud = target.FindProperty("m_Cloud");
            if (cloud != null) cloud.objectReferenceValue = renderer;

            target.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[S1Figure5] {onto.name}: {copied.Count} field(s) copied off {from.name} " +
                      $"({string.Join(", ", copied)}); m_Cloud -> its own {renderer.GetType().Name}.");

            return to;
        }

        /// <summary>
        /// The one component that makes a figure one player's.
        ///
        /// ON THE FIGURE ITSELF, not on a wrapper the way
        /// <see cref="CompositeBody.Experience.O0FigureRoleView"/> needs: that one deactivates a
        /// GameObject and so cannot be on the object it hides, whereas this writes the cloud's
        /// reveal and leaves the object active, which is also what lets the presence track go on
        /// writing the dissolve beside it.
        /// </summary>
        static bool Gate(PointCloudFigure figure, PlayerRole seenBy)
        {
            GameObject go = figure.gameObject;

            var gate = go.GetComponent<FigureRoleReveal>();
            if (gate == null)
            {
                gate = Undo.AddComponent<FigureRoleReveal>(go);
                Debug.Log($"[S1Figure5] {go.name}: FigureRoleReveal added.");
            }

            var so = new SerializedObject(gate);

            SerializedProperty role = so.FindProperty("m_VisibleTo");
            SerializedProperty cloud = so.FindProperty("m_Figure");
            if (role == null || cloud == null)
            {
                Debug.LogError("[S1Figure5] RESULT: FAIL - FigureRoleReveal has no m_VisibleTo / " +
                               "m_Figure to set. The component is not what this tool expects.");
                return false;
            }

            role.intValue = (int)seenBy;

            // Bound rather than left to Awake's GetComponent, so the inspector says which cloud
            // this gate answers for without anybody having to run the show to find out.
            cloud.objectReferenceValue = figure;

            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[S1Figure5] {go.name}: drawn only for {seenBy}.");
            return true;
        }

        // ---------------------------------------------------------------- timeline side

        /// <summary>The named track of ours, emptied of its clips, or a new one in the group.</summary>
        static FigurePresenceTrack Rebuild(TimelineAsset timeline, TrackAsset group, string name)
        {
            foreach (TrackAsset t in timeline.GetOutputTracks())
            {
                if (t is not FigurePresenceTrack found || found.name != name) continue;

                foreach (TimelineClip old in new List<TimelineClip>(found.GetClips()))
                    found.DeleteClip(old);

                return found;
            }

            return timeline.CreateTrack<FigurePresenceTrack>(group, name);
        }

        /// <summary>The first FigurePresenceTrack on Main whose name starts with this.</summary>
        static FigurePresenceTrack FindTrack(TimelineAsset timeline, string prefix)
        {
            foreach (TrackAsset t in timeline.GetOutputTracks())
                if (t is FigurePresenceTrack found && found.name.StartsWith(prefix))
                    return found;

            return null;
        }

        static PlayableDirector FindShowDirector(TimelineAsset timeline)
        {
            foreach (PlayableDirector d in Object.FindObjectsByType<PlayableDirector>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (d.playableAsset == timeline) return d;
            }

            return null;
        }

        /// <summary>GameObject.Find by path, through inactive objects too.</summary>
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
