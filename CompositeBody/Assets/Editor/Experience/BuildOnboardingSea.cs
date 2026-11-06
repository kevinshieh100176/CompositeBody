using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using CompositeBody.Experience;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Puts the sea under the onboarding room: one looping bed that is already there when the
    /// app opens -- through the lobby and the calibration, before staff press start -- holds
    /// across O-0, O-1 and O-2, and is gone again inside the blackout that opens S1.
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
    /// WHERE THE FILE CAME FROM. The master is 'O海水Loop_1107.wav', 480s of 24-bit 48kHz stereo,
    /// 138 MB. Vorbis keeps 130 MB of that off the LFS quota the voice-over is already eating,
    /// and the loop is re-cut from it; the LEVEL is the master's own.
    ///
    /// NO GAIN IS APPLIED, BY DECISION, and this is the one thing to read before touching the
    /// file. The master peaks at -50.3 dBFS and measures -64.5 LUFS integrated. The cut before it
    /// had +26 dB in the file and sat at -26.7 LUFS, so the sea as it stands is about 38 LU
    /// quieter than the version the mix was last judged against -- against voice-over that peaks
    /// at -3 dBFS, which is to say inaudible.
    ///
    /// That was asked for: the brief is the delivered level, not a matched one. It is recorded
    /// here rather than argued with, and the consequence is recorded with it, because there is NO
    /// KNOB THAT GETS IT BACK. <see cref="k_Level"/> scales an AudioSource's volume, which stops
    /// at 1, so the most the project can do is 6 dB of the 38 and that is it. If the sea needs to
    /// be heard, the gain has to go back into the file: put volume=37.8dB at the head of the
    /// filter chain below, which lands it within 0.3 dB of the previous cut.
    ///
    /// And it opens with a 6-second fade from digital silence and closes with a 5.5-second fade
    /// back to it, which as a loop is a hole in the room every time it wraps -- down past -100 dB
    /// at the floor. That would be a curiosity if the bed started with the show; it does not, it
    /// starts with the SCENE (see <see cref="AmbientBed"/>), so it has already been running for
    /// the whole lobby and calibration by the time the show starts and the wrap lands wherever it
    /// lands -- including inside the onboarding. So the fades are trimmed off and the loop is
    /// closed on itself with an equal-power crossfade:
    ///
    ///   ffmpeg -i 'O海水Loop_1107.wav' -filter_complex "
    ///     [0:a]atrim=7:473,asetpts=N/SR/TB[a]; [a]asplit=3[a1][a2][a3];
    ///     [a1]atrim=0:6,asetpts=N/SR/TB,afade=t=in:st=0:d=6:curve=qsin[h];
    ///     [a2]atrim=6:460,asetpts=N/SR/TB[m];
    ///     [a3]atrim=460:466,asetpts=N/SR/TB,afade=t=out:st=0:d=6:curve=qsin[t];
    ///     [t][h]amix=inputs=2:normalize=0[w]; [w][m]concat=n=2:v=0:a=1[out]"
    ///     -map "[out]" -c:a libvorbis -q:a 6 -ar 48000 -ac 2 'Assets/_Audio/O場景_海水.ogg'
    ///
    /// The trim at 7:473 clears both fades with a second to spare at each end. The last 6 seconds
    /// of what is left are mixed over the first 6, so playing the file end to end walks through
    /// that crossfade and comes out where it went in. 460s, 8.4 MB, -50.5 dBFS peak, and measured
    /// second by second across the wrap it stays between -66.0 and -69.0 dB RMS -- inside the
    /// recording's own ±3 dB swing, where it used to fall to nothing. qsin rather than a linear
    /// fade because the two sides are uncorrelated noise: equal power, not equal amplitude, is
    /// what keeps the level flat through the overlap.
    ///
    /// Re-cut it the same way if the recording is replaced, and re-measure the fades rather than
    /// reusing the trim points above: the two deliveries so far have had different ones, and the
    /// 1107 master arrived 9.5 dB quieter than the one before it. Unlike the first cut, this one
    /// ran on the delivered WAV, so the copy in the project has been through Vorbis once.
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
        /// Where the bed sits between its fades, written once when the holder is created. Tune it
        /// in the inspector and re-running will not take it back -- see Run.
        ///
        /// Left at the number the gain-corrected cut was tuned to, and it no longer means what it
        /// used to: the file is now at the master's own level, 38 LU down, so 0.5 lands the sea
        /// around -56 dBFS against voice-over peaking at -3 to -8. Raising this to 1 buys 6 dB of
        /// that and nothing else is available -- the class remarks say where the rest has to come
        /// from.
        /// </summary>
        const float k_Level = 0.5f;

        /// <summary>
        /// How long the sea takes to arrive, counted from the moment the scene wakes up -- NOT
        /// from a show time. The room is older than the show: the players stand in it through the
        /// lobby and the calibration, and the sea is what gives the place a size before anything
        /// is started. Eight seconds so that the app does not open on a hard wall of water.
        /// </summary>
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
                      $"up {k_InSeconds:F0}s after the scene wakes, holds through all of it, and " +
                      $"is silent at show {silentAt:F1}s. The file is {clip.length:F0}s and wraps " +
                      $"seamlessly, so it does not matter where in it the show happens to be.");

            if (last > silentAt + 0.5f)
                Debug.LogWarning($"[Sea] the onboarding runs to {last:F1}s but the bed is silent " +
                                 $"from {silentAt:F1}s -- {last - silentAt:F1}s of room with no " +
                                 "sea under it. Move the blackout, or the beat.");
        }

        /// <summary>
        /// Stream it, and keep the gain-corrected Vorbis as Vorbis.
        ///
        /// Seven and a half minutes decompressed on load would be ~85 MB of PCM resident from
        /// the moment the app opens, and a bed is the one thing that can afford to be read off
        /// disk instead: it is one voice, it starts once, and nothing about it is
        /// latency-sensitive. Quality 0.7 is ~180 kbps, which is where the source already is, so
        /// the re-encode is close to a copy.
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
                so.FindProperty("m_InSeconds").floatValue = k_InSeconds;
            }

            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[Sea] {k_Root}/{k_Holder} -> {clip.name} ({clip.length:F0}s), level " +
                      $"{so.FindProperty("m_Level").floatValue:0.00}, arrives over " +
                      $"{so.FindProperty("m_InSeconds").floatValue:F0}s from the scene waking, " +
                      $"out at show {outAt:F1}s +{outSeconds:F1}s. MainScene is dirty and NOT " +
                      "saved -- save it yourself once you have looked at what else is pending " +
                      "in it.");
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
                      $"{(source != null && source.clip != null ? source.clip.name : "NONE")} " +
                      $"({(source != null && source.clip != null ? source.clip.length : 0f):F0}s), " +
                      $"loop {(source != null && source.loop ? "on" : "OFF")}, " +
                      $"blend {(source != null ? source.spatialBlend : -1f):0.00}, " +
                      $"level {so.FindProperty("m_Level").floatValue:0.00}, " +
                      $"arrives over {so.FindProperty("m_InSeconds").floatValue:F0}s from the " +
                      $"scene waking, out at show {so.FindProperty("m_OutAt").floatValue:F1}s " +
                      $"+{so.FindProperty("m_OutSeconds").floatValue:F1}s.");
        }
    }
}
