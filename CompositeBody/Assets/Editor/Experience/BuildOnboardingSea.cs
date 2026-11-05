using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using CompositeBody.Experience;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Puts the sea under the onboarding room: one looping bed across O-0, O-1 and O-2, up at
    /// the top of the show and gone again inside the blackout that opens S1.
    ///
    /// NOT A TRACK ON THE TIMELINE, and that is the whole point -- see <see cref="AmbientBed"/>
    /// for the argument. O-1's gate pauses the director, and a room that stopped sounding while
    /// the show waited for the players would be telling them it had been switched off. So this
    /// builds an AudioSource in the scene, outside the graph the hold pauses, and hands it the
    /// two numbers it needs.
    ///
    /// THE FADE OUT IS READ, NOT TYPED. 「黑場 Onboarding → S1」 is a ScreenFadeClip on the Fade
    /// track, and its curve already says exactly when the screen is covered: this reads the
    /// clip's start and the last point its alpha is still 1, and fades the sea out between them.
    /// So the sea leaves while the screen is black and S1 opens in silence -- and a retime of
    /// the onboarding moves the fade with it on the next run, instead of leaving a number here
    /// pointing into the middle of a section. Same argument <see cref="BuildCredits"/> makes for
    /// the white-out it hangs off.
    ///
    /// WHERE THE FILE CAME FROM. Delivered as 480s of 24-bit 48kHz stereo WAV, 138 MB, peaking
    /// at -40.8 dBFS against voice-over that peaks at -3 -- inaudible at an AudioSource volume
    /// of 1, which is the ceiling. Committed instead as Vorbis with the gain already in it, which
    /// is also what keeps it off the LFS quota the voice-over is already eating:
    ///
    ///   ffmpeg -i 'O場景_海水.wav' -filter:a volume=26dB -c:a libvorbis -q:a 6 \
    ///          -ar 48000 -ac 2 'Assets/_Audio/O場景_海水.ogg'
    ///
    /// 10.4 MB, peaks at -12.6 dBFS. Re-export it the same way if the recording is replaced; the
    /// level knob is <see cref="k_Level"/>, not the file.
    ///
    /// THE LOOP IS A SAFETY NET, NOT THE DESIGN. The file is 480s and the onboarding is 400,
    /// so in an uninterrupted run the seam is never reached. It is reached when the gate at 301s
    /// holds for more than ~78 seconds, because the bed plays on real time while the show waits
    /// -- and the file begins and ends on digital silence, so the wrap is clean but audible as a
    /// couple of seconds of the sea dipping away. If that becomes the normal case, the answer is
    /// a longer recording rather than a crossfade here.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildOnboardingSea.Run
    /// </summary>
    public static class BuildOnboardingSea
    {
        const string k_Timeline = "Assets/_Timeline/Main.playable";
        const string k_Scene = "Assets/_Scenes/MainScene.unity";
        const string k_Clip = "Assets/_Audio/O場景_海水.ogg";

        /// <summary>The blackout the sea leaves inside. Owned by the show; read only.</summary>
        const string k_FadeTrack = "Fade";
        const string k_Blackout = "黑場 Onboarding → S1";

        const string k_Root = "Experience";
        const string k_Holder = "Sea Bed (Onboarding)";

        /// <summary>
        /// Where the bed sits between its fades. The file peaks at -12.6 dBFS, so this lands the
        /// sea around -18 against voice-over peaking at -3 to -8: present, and well under the
        /// line. Tune it in the inspector and re-running will not take it back -- see Run.
        /// </summary>
        const float k_Level = 0.5f;

        /// <summary>
        /// Seconds into the show the sea starts arriving, and how long it takes.
        ///
        /// Zero because the room's sound is meant to precede its space -- the screen is still
        /// black here, and O-0's own first cue is the tone at 8s, which the bed wants to be
        /// underneath by the time it lands.
        /// </summary>
        const float k_InAt = 0f;
        const float k_InSeconds = 8f;

        [MenuItem("Tools/Show/Build the Onboarding Sea")]
        public static void Run()
        {
            Debug.Log("[Sea] Starting...");

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(k_Clip);
            if (clip == null)
            {
                Debug.LogError($"[Sea] RESULT: FAIL - no AudioClip at {k_Clip}. The class " +
                               "remarks have the ffmpeg line that makes it from the delivered WAV.");
                return;
            }

            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_Timeline);
            if (timeline == null)
            {
                Debug.LogError($"[Sea] RESULT: FAIL - no TimelineAsset at {k_Timeline}.");
                return;
            }

            if (!TryReadBlackout(timeline, out float outAt, out float outSeconds)) return;

            ReportCoverage(timeline, outAt + outSeconds, clip);
            SetImportSettings();

            if (!WireScene(clip, outAt, outSeconds)) return;

            Debug.Log("[Sea] RESULT: PASS");
        }

        /// <summary>
        /// Read the fade out off the blackout clip: it starts when the screen starts going dark
        /// and ends at the last moment the screen is still fully covered.
        ///
        /// WHY THE LAST OPAQUE KEY rather than the end of the clip. The blackout's curve is an
        /// out-hold-in -- 0, 1, 1, 0 -- so its end is the next section already in full view. A
        /// sea still fading there would be the onboarding's room heard over S1's window, which
        /// is exactly the kind of overlap a blackout exists to prevent. The last opaque key is
        /// the latest instant that is still hidden.
        /// </summary>
        static bool TryReadBlackout(TimelineAsset timeline, out float outAt, out float outSeconds)
        {
            outAt = 0f;
            outSeconds = 0f;

            TimelineClip found = null;
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (track.name != k_FadeTrack) continue;
                foreach (TimelineClip c in track.GetClips())
                    if (c.displayName == k_Blackout) found = c;
            }

            if (found == null)
            {
                Debug.LogError($"[Sea] RESULT: FAIL - no clip named '{k_Blackout}' on the " +
                               $"'{k_FadeTrack}' track of {k_Timeline}. The sea's fade out is " +
                               "read off that blackout; it is not typed in here. Rename the " +
                               "clip back, or change k_Blackout to whatever it is called now.");
                return false;
            }

            if (found.asset is not ScreenFadeClip fade)
            {
                Debug.LogError($"[Sea] RESULT: FAIL - '{k_Blackout}' is not a ScreenFadeClip, " +
                               "so there is no curve to read the covered window off.");
                return false;
            }

            Keyframe[] keys = fade.template.alpha.keys;
            float lastOpaque = -1f;
            foreach (Keyframe k in keys)
                if (k.value >= 0.99f) lastOpaque = Mathf.Max(lastOpaque, k.time);

            if (lastOpaque < 0f)
            {
                Debug.LogError($"[Sea] RESULT: FAIL - '{k_Blackout}' never reaches alpha 1, so " +
                               "there is no covered moment for the sea to finish leaving in. " +
                               "Fix the blackout first: the lighting switch rides on it too.");
                return false;
            }

            outAt = (float)found.start;
            outSeconds = (float)(found.duration * Mathf.Clamp01(lastOpaque));

            Debug.Log($"[Sea] '{k_Blackout}' runs {found.start:F1}-{found.start + found.duration:F1}s " +
                      $"and is fully black by {outAt + outSeconds:F1}s, so the sea fades out " +
                      $"{outAt:F1} -> {outAt + outSeconds:F1}s ({outSeconds:F1}s).");
            return true;
        }

        /// <summary>
        /// Say what the bed actually covers against the running order, so a run can be judged
        /// without opening the Timeline window.
        ///
        /// The one thing worth being loud about is a gap: if the onboarding's last Control clip
        /// ends after the sea has gone, there is a stretch of room with no bed under it.
        /// </summary>
        static void ReportCoverage(TimelineAsset timeline, float silentAt, AudioClip clip)
        {
            double first = double.MaxValue;
            double last = 0d;
            int beats = 0;

            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (track is not ControlTrack) continue;
                if (!track.name.StartsWith("O-")) continue;

                foreach (TimelineClip c in track.GetClips())
                {
                    first = System.Math.Min(first, c.start);
                    last = System.Math.Max(last, c.end);
                    beats++;
                }
            }

            if (beats == 0)
            {
                Debug.LogWarning("[Sea] no 'O-*' Control tracks on the running order, so the " +
                                 "coverage check has nothing to compare against.");
                return;
            }

            Debug.Log($"[Sea] {beats} onboarding beat(s) run {first:F1}-{last:F1}s; the bed is " +
                      $"up from {k_InAt + k_InSeconds:F1}s and silent at {silentAt:F1}s. " +
                      $"The file is {clip.length:F0}s, so an uninterrupted run never reaches its " +
                      $"loop; a hold longer than {clip.length - silentAt:F0}s does.");

            if (last > silentAt + 0.5f)
                Debug.LogWarning($"[Sea] the onboarding runs to {last:F1}s but the bed is silent " +
                                 $"from {silentAt:F1}s -- {last - silentAt:F1}s of room with no " +
                                 "sea under it. Move the blackout, or the beat.");
        }

        /// <summary>
        /// Stream it, and keep the gain-corrected Vorbis as Vorbis.
        ///
        /// Eight minutes decompressed on load would be ~90 MB of PCM resident for a section that
        /// lasts seven minutes, and a bed is the one thing that can afford to be read off disk:
        /// it is one voice, it starts once, and nothing about it is latency-sensitive. Quality
        /// 0.7 is ~180 kbps, which is where the source already is, so the re-encode is close to
        /// a copy.
        /// </summary>
        static void SetImportSettings()
        {
            if (AssetImporter.GetAtPath(k_Clip) is not AudioImporter importer)
            {
                Debug.LogWarning($"[Sea] no AudioImporter at {k_Clip}; import settings left alone.");
                return;
            }

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            settings.preloadAudioData = false;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;

            bool changed =
                importer.defaultSampleSettings.loadType != settings.loadType ||
                importer.defaultSampleSettings.compressionFormat != settings.compressionFormat ||
                !Mathf.Approximately(importer.defaultSampleSettings.quality, settings.quality) ||
                importer.defaultSampleSettings.preloadAudioData != settings.preloadAudioData ||
                importer.forceToMono || !importer.loadInBackground;

            importer.defaultSampleSettings = settings;
            importer.forceToMono = false;
            importer.loadInBackground = true;

            if (!changed)
            {
                Debug.Log("[Sea] import settings already streaming Vorbis; not reimported.");
                return;
            }

            importer.SaveAndReimport();
            Debug.Log("[Sea] import settings set to streaming Vorbis q0.7, stereo, " +
                      "loaded in the background.");
        }

        /// <summary>
        /// Put the bed in MainScene, in the scene that is already open.
        ///
        /// OUTSIDE THE ONBOARDING, which is the one placement that matters: 'Onboarding' is
        /// switched off by an Activation track at 400s, and a bed parented under it would be cut
        /// dead mid-fade by that switch rather than fading out. It goes beside ScreenFade and
        /// LightingDirector instead, under 'Experience', which nothing deactivates.
        ///
        /// Refuses to open the scene, and leaves it dirty rather than saving it: MainScene is
        /// where the retime in progress lives and whether somebody else's unsaved work goes to
        /// disk is not this tool's decision. Same stance as <see cref="BuildCredits"/>.
        ///
        /// RE-RUNNABLE WITHOUT TAKING THE MIX BACK. The level and the fade in are only written
        /// when the holder is created, because those are the two numbers somebody will sit in
        /// the headset and tune; the fade out is written every run, because it is read off the
        /// timeline and is not a matter of taste.
        /// </summary>
        static bool WireScene(AudioClip clip, float outAt, float outSeconds)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != k_Scene)
            {
                Debug.LogError($"[Sea] RESULT: FAIL - the open scene is '{scene.path}' and the " +
                               $"bed belongs in {k_Scene}. Open MainScene and run this again.");
                return false;
            }

            GameObject root = null;
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == k_Root) root = go;

            if (root == null)
            {
                Debug.LogError($"[Sea] RESULT: FAIL - no '{k_Root}' root in MainScene. " +
                               "ScreenFade and LightingDirector live under it and the bed " +
                               "belongs beside them, outside anything the show deactivates.");
                return false;
            }

            Transform holder = root.transform.Find(k_Holder);
            bool created = holder == null;
            if (created)
            {
                var go = new GameObject(k_Holder);
                Undo.RegisterCreatedObjectUndo(go, "Create the onboarding sea");
                go.transform.SetParent(root.transform, false);
                holder = go.transform;
                Debug.Log($"[Sea] created {k_Root}/{k_Holder}.");
            }

            var source = holder.GetComponent<AudioSource>();
            if (source == null) source = Undo.AddComponent<AudioSource>(holder.gameObject);

            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;

            // 2D on purpose. The sea is the room this pair is standing in, not an object in it:
            // a positioned source would swing across the head as a player turned, and the two of
            // them stand metres apart and must hear the same room.
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.priority = 0;
            source.ignoreListenerPause = true;

            var bed = holder.GetComponent<AmbientBed>();
            if (bed == null) bed = Undo.AddComponent<AmbientBed>(holder.gameObject);

            var so = new SerializedObject(bed);
            so.FindProperty("m_Source").objectReferenceValue = source;
            so.FindProperty("m_OutAt").floatValue = outAt;
            so.FindProperty("m_OutSeconds").floatValue = outSeconds;

            if (created)
            {
                so.FindProperty("m_Level").floatValue = k_Level;
                so.FindProperty("m_InAt").floatValue = k_InAt;
                so.FindProperty("m_InSeconds").floatValue = k_InSeconds;
            }

            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[Sea] {k_Root}/{k_Holder} -> {clip.name}, level " +
                      $"{so.FindProperty("m_Level").floatValue:0.00}, in " +
                      $"{so.FindProperty("m_InAt").floatValue:F0}s +" +
                      $"{so.FindProperty("m_InSeconds").floatValue:F0}s, out {outAt:F1}s " +
                      $"+{outSeconds:F1}s. MainScene is dirty and NOT saved -- save it yourself " +
                      "once you have looked at what else is pending in it.");
            return true;
        }

        /// <summary>
        /// Read the bed's state back out of the open scene and print it. Track names and clip
        /// times do not pair up in the .playable file's own order, so grepping tells you nothing.
        /// </summary>
        [MenuItem("Tools/Show/Report the Onboarding Sea")]
        public static void Report()
        {
            Scene scene = SceneManager.GetActiveScene();
            GameObject holder = null;
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name != k_Root) continue;
                Transform t = go.transform.Find(k_Holder);
                if (t != null) holder = t.gameObject;
            }

            if (holder == null)
            {
                Debug.Log($"[Sea] no {k_Root}/{k_Holder} in '{scene.name}'. Run " +
                          "Tools/Show/Build the Onboarding Sea with MainScene open.");
                return;
            }

            var bed = holder.GetComponent<AmbientBed>();
            var source = holder.GetComponent<AudioSource>();
            var so = new SerializedObject(bed);

            Debug.Log($"[Sea] {k_Root}/{k_Holder}: clip " +
                      $"{(source != null && source.clip != null ? source.clip.name : "NONE")}, " +
                      $"loop {(source != null && source.loop ? "on" : "OFF")}, " +
                      $"blend {(source != null ? source.spatialBlend : -1f):0.00}, " +
                      $"level {so.FindProperty("m_Level").floatValue:0.00}, " +
                      $"in {so.FindProperty("m_InAt").floatValue:F0}s " +
                      $"+{so.FindProperty("m_InSeconds").floatValue:F0}s, " +
                      $"out {so.FindProperty("m_OutAt").floatValue:F1}s " +
                      $"+{so.FindProperty("m_OutSeconds").floatValue:F1}s.");
        }
    }
}
