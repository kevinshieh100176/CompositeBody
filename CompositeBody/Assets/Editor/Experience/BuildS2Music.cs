using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using CompositeBody.Experience;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Puts the music under S2: one looping bed that arrives inside the blackout that closes S1,
    /// holds through every beat of S2 including the gated ones, and is gone again inside the
    /// blackout that opens S3.
    ///
    /// NOT A TRACK ON THE TIMELINE, same argument <see cref="BuildOnboardingSea"/> makes for the
    /// sea and the more pressing one here. S2 is where the interaction modules live -- Object
    /// Assembly's phases, the home interaction -- and a gated beat ends when both players have
    /// done the thing, which <see cref="ShowTimelineDriver.Apply"/> expresses by PAUSING the
    /// director. Music on an AudioTrack stops dead at that pause. Music is the one thing in the
    /// mix that cannot stop dead: a cut in the middle of a phrase is heard as a fault, and it
    /// lands at exactly the moment the players are being asked to work something out. So this
    /// builds an AudioSource in the scene, outside the graph the hold pauses.
    ///
    /// THE WINDOW IS READ, NOT TYPED. Both ends come off Main.playable:
    ///
    ///   in at    the start of the first 'S2-*' Control clip (767s) -- where S2 begins, and
    ///            where S1's narration ends, which is the earlier of the two constraints
    ///   in over  until 「黑場 S1 → S2」 hands the picture back (772s), so the music is at
    ///            level on the frame S2 becomes visible and arrived in the dark
    ///   out at   the start of 「黑場 S2 → S3」 (996s), where the picture starts leaving
    ///   out over until that blackout is fully opaque (+6.7s), so S3 opens on silence
    ///
    /// A retime of the back half therefore moves the music with it on the next run, instead of
    /// leaving a number here pointing into the middle of a section.
    ///
    /// THE WRAP IS NOT REACHED IN PRACTICE, and the file is left as delivered because of it. S2
    /// is 234s of authored time and the loop is 496s, so the show runs out of section less than
    /// half way through the file. Looping is still on, as the thing that keeps the room alive if
    /// the pair sit in a hold for four minutes, but it is a safety net rather than the normal
    /// case -- which is why this does NOT crossfade the file onto itself the way the sea is cut.
    /// An equal-power crossfade is free on broadband noise and expensive on music: it would put
    /// six seconds of the opening under the closing, out of time with it. If a hold ever does run
    /// long enough to reach the wrap, the fix is a loop cut by whoever wrote the piece, not a
    /// filter graph here.
    ///
    /// WHERE THE FILE CAME FROM. Delivered as 496s of 24-bit 48kHz stereo, 142 MB, peaking at
    /// -17.3 dBFS with a flat -30 to -34 dB RMS from end to end -- already a bed, with no fades
    /// at either end to trim and no gain needed. So the only step is Vorbis, which keeps 131 MB
    /// off the LFS quota the voice-over is already eating:
    ///
    ///   ffmpeg -i 'S2音樂Loop_1107.wav' -c:a libvorbis -q:a 6 -ar 48000 -ac 2 \
    ///     'Assets/_Audio/S2場景_音樂.ogg'
    ///
    /// 10.8 MB, and one Vorbis pass rather than the two the sea's copy has been through.
    ///
    /// THE LEVEL IS A GUESS AND WANTS A HEADSET. <see cref="k_Level"/> at 0.5 puts the music's
    /// peak around -23 dBFS against voice-over peaking at -3 to -8 -- a defensible starting point
    /// and nothing more, because music under narration is a mix decision and not an arithmetic
    /// one. Tune it in the inspector; re-running will not take it back (see Run).
    ///
    /// Note that this file, unlike the sea's, is at a level the project can work with: it was
    /// delivered as a bed and needed no gain, so the knob is a real knob here.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildS2Music.Run
    /// </summary>
    public static class BuildS2Music
    {
        const string k_Timeline = "Assets/_Timeline/Main.playable";
        const string k_Scene = "Assets/_Scenes/MainScene.unity";
        const string k_Clip = "Assets/_Audio/S2場景_音樂.ogg";

        /// <summary>The two blackouts the music arrives and leaves inside. Read only.</summary>
        const string k_FadeTrack = "Fade";
        const string k_InBlackout = "黑場 S1 → S2";
        const string k_OutBlackout = "黑場 S2 → S3";

        /// <summary>
        /// The section this bed belongs to. Its Control tracks are what say where S2 starts and
        /// ends; there is no other record of that in the project.
        /// </summary>
        const string k_SectionPrefix = "S2-";

        const string k_Root = "Experience";
        const string k_Holder = "Music Bed (S2)";

        /// <summary>
        /// Where the bed sits between its fades, written once when the holder is created.
        ///
        /// Nominally the sea's number, but do not read anything into the two matching: the sea's
        /// file is deliberately left at a master level 38 LU below this one (see
        /// <see cref="BuildOnboardingSea"/>), so the same 0.5 means something completely
        /// different there. This one lands the music's peak around -23 dBFS against voice-over
        /// peaking at -3 to -8, which is a starting point and nothing more -- music under
        /// narration is a mix decision, not an arithmetic one. Audition it.
        /// </summary>
        const float k_Level = 0.5f;

        [MenuItem("Tools/Show/Build the S2 Music")]
        public static void Run()
        {
            Debug.Log("[S2 Music] Starting...");

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(k_Clip);
            if (clip == null)
            {
                Debug.LogError($"[S2 Music] RESULT: FAIL - no AudioClip at {k_Clip}. The class " +
                               "remarks have the ffmpeg line that makes it from the delivered WAV.");
                return;
            }

            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(k_Timeline);
            if (timeline == null)
            {
                Debug.LogError($"[S2 Music] RESULT: FAIL - no TimelineAsset at {k_Timeline}.");
                return;
            }

            if (!TryReadSection(timeline, out float sectionStart, out float sectionEnd)) return;
            if (!TryReadArrival(timeline, sectionStart, out float inAt, out float inSeconds)) return;
            if (!TryReadDeparture(timeline, out float outAt, out float outSeconds)) return;

            ReportCoverage(clip, sectionStart, sectionEnd, inAt, inSeconds, outAt, outSeconds);
            SetImportSettings();

            if (!WireScene(clip, inAt, inSeconds, outAt, outSeconds)) return;

            Debug.Log("[S2 Music] RESULT: PASS");
        }

        /// <summary>
        /// Where S2 runs, off its own Control tracks. The earliest start and the latest end of
        /// every 'S2-*' track: the section is whatever those clips cover, which is the only
        /// definition of it the project actually holds.
        /// </summary>
        static bool TryReadSection(TimelineAsset timeline, out float start, out float end)
        {
            double first = double.MaxValue;
            double last = 0d;
            int beats = 0;
            var names = new List<string>();

            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (track is not ControlTrack) continue;
                if (!track.name.StartsWith(k_SectionPrefix)) continue;

                foreach (TimelineClip c in track.GetClips())
                {
                    first = System.Math.Min(first, c.start);
                    last = System.Math.Max(last, c.end);
                    beats++;
                }
                names.Add(track.name);
            }

            start = (float)first;
            end = (float)last;

            if (beats == 0)
            {
                Debug.LogError($"[S2 Music] RESULT: FAIL - no '{k_SectionPrefix}*' Control tracks " +
                               $"on {k_Timeline}, so there is no S2 for the bed to cover. If the " +
                               "beats have been renamed, change k_SectionPrefix to match.");
                return false;
            }

            Debug.Log($"[S2 Music] S2 is {beats} beat(s) on {names.Count} track(s) running " +
                      $"{start:F1}-{end:F1}s ({end - start:F0}s): {string.Join(", ", names)}.");
            return true;
        }

        /// <summary>
        /// The arrival: it starts where S2 does and ends where the picture comes back.
        ///
        /// WHY NOT EARLIER. The blackout opens at 762s and S2's first beat is at 767s, so there
        /// are five seconds of covered screen before the section where the music could in
        /// principle sneak in. It must not: S1's narration tracks run to 767s, and music under
        /// the last line of the previous section is that line's music, not this one's.
        ///
        /// WHY NOT LATER. The screen is handed back at 772s. A bed still climbing when the room
        /// appears is heard as the music chasing the picture; arriving in the dark and being
        /// simply there when the lights come up is what a section change is supposed to feel like.
        ///
        /// Which makes the fade the gap between the two -- five seconds, as it happens, and if a
        /// retime makes it one second this says so instead of sounding wrong in the headset.
        /// </summary>
        static bool TryReadArrival(TimelineAsset timeline, float sectionStart,
                                   out float inAt, out float inSeconds)
        {
            inAt = sectionStart;
            inSeconds = 0f;

            if (!TryFindFade(timeline, k_InBlackout, out TimelineClip clip, out ScreenFadeClip fade))
                return false;

            // The last key, which for an out-hold-in curve is the picture fully back.
            Keyframe[] keys = fade.template.alpha.keys;
            if (keys.Length == 0)
            {
                Debug.LogError($"[S2 Music] RESULT: FAIL - '{k_InBlackout}' has an empty alpha " +
                               "curve, so there is no moment to arrive by.");
                return false;
            }

            float pictureBack = (float)(clip.start + clip.duration * keys[keys.Length - 1].time);
            inSeconds = pictureBack - inAt;

            if (inSeconds <= 0f)
            {
                Debug.LogWarning($"[S2 Music] S2 starts at {inAt:F1}s but '{k_InBlackout}' has " +
                                 $"already handed the picture back by {pictureBack:F1}s, so " +
                                 "there is no dark to arrive in. The music will snap on in view. " +
                                 "Move the blackout, or the beat.");
                inSeconds = 0f;
                return true;
            }

            Debug.Log($"[S2 Music] '{k_InBlackout}' runs {clip.start:F1}-{clip.start + clip.duration:F1}s " +
                      $"and hands the picture back at {pictureBack:F1}s, so the music arrives " +
                      $"{inAt:F1} -> {pictureBack:F1}s ({inSeconds:F1}s) -- the last of it in the dark.");
            return true;
        }

        /// <summary>
        /// The departure, read exactly the way the sea's is: from the start of the blackout to
        /// the last moment it is still fully opaque. See <see cref="BuildOnboardingSea"/> for why
        /// the last opaque key and not the end of the clip.
        /// </summary>
        static bool TryReadDeparture(TimelineAsset timeline, out float outAt, out float outSeconds)
        {
            outAt = 0f;
            outSeconds = 0f;

            if (!TryFindFade(timeline, k_OutBlackout, out TimelineClip clip, out ScreenFadeClip fade))
                return false;

            float lastOpaque = -1f;
            foreach (Keyframe k in fade.template.alpha.keys)
                if (k.value >= 0.99f) lastOpaque = Mathf.Max(lastOpaque, k.time);

            if (lastOpaque < 0f)
            {
                Debug.LogError($"[S2 Music] RESULT: FAIL - '{k_OutBlackout}' never reaches alpha " +
                               "1, so there is no covered moment for the music to finish leaving " +
                               "in. Fix the blackout first: the wall swap rides on it too.");
                return false;
            }

            outAt = (float)clip.start;
            outSeconds = (float)(clip.duration * Mathf.Clamp01(lastOpaque));

            Debug.Log($"[S2 Music] '{k_OutBlackout}' runs {clip.start:F1}-{clip.start + clip.duration:F1}s " +
                      $"and is fully black by {outAt + outSeconds:F1}s, so the music fades out " +
                      $"{outAt:F1} -> {outAt + outSeconds:F1}s ({outSeconds:F1}s).");
            return true;
        }

        /// <summary>One named ScreenFadeClip off the Fade track, or a loud failure.</summary>
        static bool TryFindFade(TimelineAsset timeline, string name,
                                out TimelineClip clip, out ScreenFadeClip fade)
        {
            clip = null;
            fade = null;

            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (track.name != k_FadeTrack) continue;
                foreach (TimelineClip c in track.GetClips())
                    if (c.displayName == name) clip = c;
            }

            if (clip == null)
            {
                Debug.LogError($"[S2 Music] RESULT: FAIL - no clip named '{name}' on the " +
                               $"'{k_FadeTrack}' track of {k_Timeline}. The music's fades are " +
                               "read off the blackouts; they are not typed in here. Rename the " +
                               "clip back, or change the constant to whatever it is called now.");
                return false;
            }

            fade = clip.asset as ScreenFadeClip;
            if (fade == null)
            {
                Debug.LogError($"[S2 Music] RESULT: FAIL - '{name}' is not a ScreenFadeClip, so " +
                               "there is no curve to read the covered window off.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Say what the bed actually covers against the running order, so a run can be judged
        /// without opening the Timeline window.
        ///
        /// Two things worth being loud about. A gap at either end -- music that has left before
        /// S2 has, or arrives after it started -- and the wrap, which says how much hold the
        /// section can absorb before the loop point is reached at all.
        /// </summary>
        static void ReportCoverage(AudioClip clip, float sectionStart, float sectionEnd,
                                   float inAt, float inSeconds, float outAt, float outSeconds)
        {
            float silentAt = outAt + outSeconds;
            float authored = sectionEnd - sectionStart;

            Debug.Log($"[S2 Music] the bed is up from show {inAt:F1}s (+{inSeconds:F1}s), holds " +
                      $"through every beat including the gated ones, and is silent at " +
                      $"{silentAt:F1}s. The file is {clip.length:F0}s against {authored:F0}s of " +
                      $"authored S2, so the loop point is only reached if holds add " +
                      $"{clip.length - authored:F0}s of wall time to the section.");

            if (inAt > sectionStart + 0.5f)
                Debug.LogWarning($"[S2 Music] S2 starts at {sectionStart:F1}s but the music only " +
                                 $"starts arriving at {inAt:F1}s -- {inAt - sectionStart:F1}s of " +
                                 "section with no music under it.");

            if (sectionEnd > silentAt + 0.5f)
                Debug.LogWarning($"[S2 Music] S2 runs to {sectionEnd:F1}s but the music is silent " +
                                 $"from {silentAt:F1}s -- {sectionEnd - silentAt:F1}s of section " +
                                 "with no music under it. Move the blackout, or the beat.");

            if (clip.length < authored)
                Debug.LogWarning($"[S2 Music] the file is {clip.length:F0}s and S2 is " +
                                 $"{authored:F0}s, so the loop point IS reached in a clean run. " +
                                 "The file is not cut as a seamless loop -- see the class " +
                                 "remarks -- so get one that is.");
        }

        /// <summary>
        /// Stream it, and keep the Vorbis as Vorbis.
        ///
        /// Eight and a quarter minutes decompressed on load would be ~91 MB of PCM resident, and
        /// a bed is the one thing that can afford to be read off disk instead: one voice, started
        /// once, nothing latency-sensitive about it. Quality 0.7 is ~180 kbps, which is where the
        /// file already is, so the re-encode is close to a copy.
        ///
        /// preloadAudioData off matters more here than it does for the sea: this clip is silent
        /// for the first twelve minutes of the show, and there is no reason for it to be in memory
        /// while the onboarding and S1 run.
        /// </summary>
        static void SetImportSettings()
        {
            if (AssetImporter.GetAtPath(k_Clip) is not AudioImporter importer)
            {
                Debug.LogWarning($"[S2 Music] no AudioImporter at {k_Clip}; import settings left " +
                                 "alone.");
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
                Debug.Log("[S2 Music] import settings already streaming Vorbis; not reimported.");
                return;
            }

            importer.SaveAndReimport();
            Debug.Log("[S2 Music] import settings set to streaming Vorbis q0.7, stereo, loaded " +
                      "in the background.");
        }

        /// <summary>
        /// Put the bed in MainScene, in the scene that is already open.
        ///
        /// OUTSIDE S2, which is the placement that matters. The set S2 shares with S3 is switched
        /// by Activation tracks -- one of them turns off at 1001s, inside the very blackout the
        /// music is still fading in -- and a bed parented under any of them would be cut dead by
        /// that switch rather than fading out. It goes beside ScreenFade, LightingDirector and
        /// the sea instead, under 'Experience', which nothing deactivates.
        ///
        /// Refuses to open the scene, and leaves it dirty rather than saving it: MainScene is
        /// where the retime in progress lives and whether somebody else's unsaved work goes to
        /// disk is not this tool's decision. Same stance as <see cref="BuildOnboardingSea"/>.
        ///
        /// RE-RUNNABLE WITHOUT TAKING THE MIX BACK. The level is only written when the holder is
        /// created, because that is the number somebody will sit in the headset and tune; the
        /// window is written every run, because it is read off the timeline and is not a matter
        /// of taste.
        /// </summary>
        static bool WireScene(AudioClip clip, float inAt, float inSeconds,
                              float outAt, float outSeconds)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != k_Scene)
            {
                Debug.LogError($"[S2 Music] RESULT: FAIL - the open scene is '{scene.path}' and " +
                               $"the bed belongs in {k_Scene}. Open MainScene and run this again.");
                return false;
            }

            GameObject root = null;
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == k_Root) root = go;

            if (root == null)
            {
                Debug.LogError($"[S2 Music] RESULT: FAIL - no '{k_Root}' root in MainScene. " +
                               "ScreenFade, LightingDirector and the onboarding sea live under " +
                               "it and the music belongs beside them, outside anything the show " +
                               "deactivates.");
                return false;
            }

            Transform holder = root.transform.Find(k_Holder);
            bool created = holder == null;
            if (created)
            {
                var go = new GameObject(k_Holder);
                Undo.RegisterCreatedObjectUndo(go, "Create the S2 music");
                go.transform.SetParent(root.transform, false);
                holder = go.transform;
                Debug.Log($"[S2 Music] created {k_Root}/{k_Holder}.");
            }

            var source = holder.GetComponent<AudioSource>();
            if (source == null) source = Undo.AddComponent<AudioSource>(holder.gameObject);

            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;

            // 2D on purpose. The music is not in the room with them -- there is nothing in S2 it
            // comes out of -- and the two players stand metres apart and must hear the same mix.
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.priority = 0;
            source.ignoreListenerPause = true;

            var bed = holder.GetComponent<AmbientBed>();
            if (bed == null) bed = Undo.AddComponent<AmbientBed>(holder.gameObject);

            var so = new SerializedObject(bed);
            so.FindProperty("m_Source").objectReferenceValue = source;
            so.FindProperty("m_InAt").floatValue = inAt;
            so.FindProperty("m_InSeconds").floatValue = inSeconds;
            so.FindProperty("m_OutAt").floatValue = outAt;
            so.FindProperty("m_OutSeconds").floatValue = outSeconds;

            if (created) so.FindProperty("m_Level").floatValue = k_Level;

            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[S2 Music] {k_Root}/{k_Holder} -> {clip.name} ({clip.length:F0}s), level " +
                      $"{so.FindProperty("m_Level").floatValue:0.00}, in at show {inAt:F1}s " +
                      $"+{inSeconds:F1}s, out at show {outAt:F1}s +{outSeconds:F1}s. MainScene is " +
                      "dirty and NOT saved -- save it yourself once you have looked at what else " +
                      "is pending in it.");
            return true;
        }

        /// <summary>
        /// Read the bed's state back out of the open scene and print it. Track names and clip
        /// times do not pair up in the .playable file's own order, so grepping tells you nothing.
        /// </summary>
        [MenuItem("Tools/Show/Report the S2 Music")]
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
                Debug.Log($"[S2 Music] no {k_Root}/{k_Holder} in '{scene.name}'. Run " +
                          "Tools/Show/Build the S2 Music with MainScene open.");
                return;
            }

            var bed = holder.GetComponent<AmbientBed>();
            var source = holder.GetComponent<AudioSource>();
            var so = new SerializedObject(bed);

            Debug.Log($"[S2 Music] {k_Root}/{k_Holder}: clip " +
                      $"{(source != null && source.clip != null ? source.clip.name : "NONE")} " +
                      $"({(source != null && source.clip != null ? source.clip.length : 0f):F0}s), " +
                      $"loop {(source != null && source.loop ? "on" : "OFF")}, " +
                      $"blend {(source != null ? source.spatialBlend : -1f):0.00}, " +
                      $"level {so.FindProperty("m_Level").floatValue:0.00}, " +
                      $"in at show {so.FindProperty("m_InAt").floatValue:F1}s " +
                      $"+{so.FindProperty("m_InSeconds").floatValue:F1}s, " +
                      $"out at show {so.FindProperty("m_OutAt").floatValue:F1}s " +
                      $"+{so.FindProperty("m_OutSeconds").floatValue:F1}s.");
        }
    }
}
