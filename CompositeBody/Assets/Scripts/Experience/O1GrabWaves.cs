using System;
using System.Collections.Generic;
using UnityEngine;
using CompositeBody.Diagnostics;
using CompositeBody.Multiplayer;

namespace CompositeBody.Experience
{
    /// <summary>
    /// O-1's objects, one wave at a time -- except that the two halves of a wave run on
    /// different clocks, and that is the whole design.
    ///
    /// WHAT BRINGS YOUR NEXT OBJECT IN IS YOUR OWN BOX. 「玩家自己把自己的物品放進箱子後就會跑出下一樣
    /// 自己的物品」 -- each player's chain of objects is theirs alone: put your lamp away and your
    /// jacket arrives, whatever the other player is doing. None of that crosses the network and
    /// none of it needs to, because you cannot see their objects and they cannot see yours.
    ///
    /// WHAT THE SPEECH WAITS FOR IS BOTH OF YOU. 「vo還是要兩個玩家都抓過自己的那個後才會出現」 -- a
    /// line answers both players HOLDING that wave's object, which is one fact about two
    /// machines and the only thing in this beat that needs a network at all. A grab is reported
    /// as a task through <see cref="StoryProgressManager"/>, which attributes it to whoever sent
    /// it and raises <see cref="StoryProgressManager.onTaskBeatUnlocked"/> on EVERY machine once
    /// both roles are in. That event is what speaks, so the line lands on both headsets on the
    /// same frame -- 「與B共同聆聽」 -- without a second channel for it.
    ///
    /// WHICH MEANS THE TWO HALVES DRIFT APART, AND THE LINES QUEUE. A quick pair can be holding
    /// the jacket while the lamp's line is still playing: 「然後照順序播放完」. So an unlock does not
    /// speak, it joins a queue, and the queue is played IN WAVE ORDER -- the running order the
    /// lines were written in -- never in the order they happened to unlock. One speaker, one
    /// line at a time.
    ///
    /// A WAVE WITH NO OBJECTS IS THE PAUSE. v4's third row -- 「兩人要將紙箱封箱」 -- is a line and
    /// nothing else: 「這裡先不要加互動」. It has no object to bring in and nobody to wait for, so it
    /// speaks as soon as every line before it has been heard, which is 「前面兩個vo都播放完成後會播放
    /// a3/b3」. And 「vo撥放完成才會解鎖停頓點」: <see cref="O1DropSequence"/> gates the show's hold on
    /// <see cref="finished"/>, so O-1 does not end until that last line has.
    ///
    /// ON A DESK, WITH NO SESSION, there is no StoryProgressManager and no other player. The
    /// fallback is the same one the rest of the beat uses: with no role assigned, this machine
    /// owns both sides, so a wave unlocks when both of its stations have been grabbed here.
    /// That is the only way to walk the beat alone, and it is how every debug run of it happens.
    ///
    /// THE FIRST WAVE IS NOT BROUGHT IN HERE. The lamps arrive on O-1's own timeline, after the
    /// cartons -- that is authored, it has a shape, and a curve you can drag is worth more than
    /// a number in a component. Only the waves AFTER the first are brought in by this, because
    /// when they arrive depends on when a player put the last one away.
    ///
    /// WHICH MEANS A LATER WAVE'S OBJECTS MUST NOT BE ON THE ANIMATION TRACK. An animation track
    /// writes the properties it owns on every frame its director is evaluated, so a curve and
    /// this component would fight over the same float and the track would win. One owner: the
    /// first wave's is the timeline, every later wave's is this.
    /// </summary>
    [DisallowMultipleComponent]
    public class O1GrabWaves : MonoBehaviour, IDebugInteraction
    {
        /// <summary>One player's own recording of a moment the other player hears differently.</summary>
        [Serializable]
        public class RoleLine
        {
            public PlayerRole role = PlayerRole.Player1;
            public AudioClip clip;
        }

        /// <summary>
        /// One player's half of a wave: the object they are shown, and what fades it in.
        ///
        /// The two belong together rather than sitting in parallel arrays, because that is how
        /// the jacket ends up arriving for the player who was holding the lamp.
        /// </summary>
        [Serializable]
        public class Side
        {
            [Tooltip("Whose half of the wave this is.")]
            public PlayerRole role = PlayerRole.Player1;

            [Tooltip("The station holding this player's copy of the object. Its grab reports " +
                     "this player's half of 'both of them are holding one'.")]
            public O1GrabStation station;

            [Tooltip("This object's presence, faded in once the PREVIOUS wave's line has " +
                     "finished -- for both players at once. Empty on the first wave, which the " +
                     "timeline brings in instead.")]
            public PropPresence appears;

        }

        /// <summary>One object for each player, and the line the two of them are owed for it.</summary>
        [Serializable]
        public class Wave
        {
            [Tooltip("What this wave is called in the log and in the debug panel.")]
            public string name = "";

            [Tooltip("The task that means 'both players are holding this one'. Reported per " +
                     "role; the server raises the unlock once both halves are in. It must be " +
                     "unique per wave, or the second wave unlocks on the first wave's grabs.")]
            public string taskId = "";

            [Tooltip("One entry per player. Empty means this wave is a pause: a line with no " +
                     "object behind it, spoken as soon as the lines before it have been heard.")]
            public Side[] sides = new Side[0];

            [Tooltip("What is said once both players have it in hand, when the two of them " +
                     "hear the SAME recording -- 「與B共同聆聽」. Leave it empty when they each " +
                     "have their own, and fill ownLines instead.")]
            public AudioClip line;

            [Tooltip("What is said when the two players hear DIFFERENT recordings of the same " +
                     "moment, one per role -- O-A3 / O-B3. Takes precedence over line. Each " +
                     "machine plays its own role's clip; with no role assigned it plays the " +
                     "first, so the beat can still be walked alone.")]
            public RoleLine[] ownLines = new RoleLine[0];

            [HideInInspector] public bool unlocked;
            [HideInInspector] public bool spoken;

            /// <summary>Whether this is a line with no object behind it -- the pause.</summary>
            public bool isPause => wired == 0;

            /// <summary>Stations that are actually wired, so a hole in the list is not a hostage.</summary>
            public int wired
            {
                get
                {
                    int n = 0;
                    foreach (Side side in sides)
                    {
                        if (side != null && side.station != null) n++;
                    }
                    return n;
                }
            }

            /// <summary>How many of them have been picked up on THIS machine.</summary>
            public int held
            {
                get
                {
                    int n = 0;
                    foreach (Side side in sides)
                    {
                        if (side != null && side.station != null && side.station.grabbed) n++;
                    }
                    return n;
                }
            }
        }

        [SerializeField, Tooltip("The objects of O-1, in the order they are to appear -- which " +
                                 "is also the order their lines are heard in.")]
        Wave[] m_Waves = new Wave[0];

        [SerializeField, Tooltip("Speaks the lines. Made on this object if left empty. 2D: " +
                                 "these are the room speaking to both players, not a thing in " +
                                 "it that one of them is standing nearer to.")]
        AudioSource m_Source;

        [SerializeField, Min(0f), Tooltip("Silence between one line ending and the next " +
                                          "beginning. A queue that runs the clips end to end " +
                                          "sounds like one long file.")]
        float m_Gap = 0.5f;

        [SerializeField, Min(0.01f), Tooltip("Seconds an object takes to dissolve in once its " +
                                             "player has put the last one away.")]
        float m_FadeSeconds = 3f;

        /// <summary>One object on its way in, for one player.</summary>
        struct Arrival
        {
            public PropPresence presence;
            public float since;
            public string label;
        }

        readonly List<Arrival> m_Arrivals = new();

        /// <summary>The wave now sounding, or -1 between lines.</summary>
        int m_Speaking = -1;

        /// <summary>When the line now sounding is due to end, on the show clock.</summary>
        float m_LineEndsAt = -1f;

        readonly List<string> m_Reported = new();

        /// <summary>Whether a line is sounding right now.</summary>
        public bool speaking => m_Speaking >= 0 && ExperienceClock.now < m_LineEndsAt;

        /// <summary>Whether every line has been heard and nothing is still on its way in.</summary>
        public bool finished
        {
            get
            {
                foreach (Wave wave in m_Waves)
                {
                    if (wave != null && !wave.spoken) return false;
                }
                return m_Speaking < 0 && m_Arrivals.Count == 0;
            }
        }

        void Awake()
        {
            Resolve();

            foreach (Wave wave in m_Waves)
            {
                if (wave == null) continue;
                foreach (Side side in wave.sides)
                {
                    if (side == null) continue;
                    if (side.station != null) side.station.onGrabbed += HandleGrabbed;
                }
            }

            // Awake, not OnEnable, so a beat that is switched off still has a debug row rather
            // than looking like an interaction nobody built.
            InteractionDebug.Register(this);
        }

        void OnEnable()
        {
            StoryProgressManager progress = StoryProgressManager.Instance;
            if (progress != null) progress.onTaskBeatUnlocked += HandleUnlocked;
        }

        void OnDisable()
        {
            StoryProgressManager progress = StoryProgressManager.Instance;
            if (progress != null) progress.onTaskBeatUnlocked -= HandleUnlocked;
        }

        void OnDestroy()
        {
            foreach (Wave wave in m_Waves)
            {
                if (wave == null) continue;
                foreach (Side side in wave.sides)
                {
                    if (side == null) continue;
                    if (side.station != null) side.station.onGrabbed -= HandleGrabbed;
                }
            }

            InteractionDebug.Unregister(this);
        }

        void Resolve()
        {
            if (m_Source == null) m_Source = GetComponent<AudioSource>();
            if (m_Source == null)
            {
                m_Source = gameObject.AddComponent<AudioSource>();
                m_Source.playOnAwake = false;
                m_Source.loop = false;
                m_Source.spatialBlend = 0f;
            }

            // Every wave after the first starts absent, whatever the prefab was left saying.
            // The authored state of a thing that has not arrived yet is "not here", and a wave
            // that shipped at presence 1 would be standing in the room from the first frame of
            // the show -- there is no timeline hiding it, because this owns it.
            for (int i = 1; i < m_Waves.Length; i++) SetPresence(m_Waves[i], 0f);

            if (m_Waves.Length == 0)
                Debug.LogWarning($"[O1Waves] '{name}' has no waves wired, so nothing is ever " +
                                 "said and no object follows the first.", this);
        }

        // ------------------------------------------------------------------ the grabs

        /// <summary>
        /// This player picked their object up. Report it and let the server decide whether that
        /// makes two -- or, with nobody to ask, decide it here.
        ///
        /// ANY WAVE, NOT JUST THE ONE BEING SPOKEN. The objects and the speech no longer run in
        /// step: a player can be holding the jacket while the lamp's line is still playing, and
        /// that grab is exactly the half of wave 2 this has to report.
        /// </summary>
        void HandleGrabbed(O1GrabStation station)
        {
            int index = IndexOf(station);
            if (index < 0) return;

            Wave wave = m_Waves[index];
            if (wave.unlocked) return;

            Debug.Log($"[O1Waves] {Label(wave, index)}: {station.owner} is holding theirs " +
                      $"({wave.held}/{wave.wired} on this machine).", this);

            StoryProgressManager progress = StoryProgressManager.Instance;
            if (progress == null)
            {
                // A DESK TEST. No session, so no other player and no server to attribute
                // anything to: this machine owns both sides, and both of them being in hand is
                // the whole condition. Same fallback O1DropSequence uses for the same reason.
                if (wave.held >= wave.wired && wave.wired > 0) Unlock(index, "alone");
                return;
            }

            if (string.IsNullOrWhiteSpace(wave.taskId))
            {
                Debug.LogWarning($"[O1Waves] {Label(wave, index)} has no task id, so 'both " +
                                 "players are holding it' cannot cross the network. Speaking on " +
                                 "this machine's grabs alone.", this);
                if (wave.held >= wave.wired && wave.wired > 0) Unlock(index, "no task id");
                return;
            }

            // Once per task. The RPC is idempotent server-side, but a second report for a role
            // already marked done is a round trip that says nothing.
            if (m_Reported.Contains(wave.taskId)) return;
            m_Reported.Add(wave.taskId);

            progress.ReportTaskCompleteRpc(wave.taskId);
            Debug.Log($"[O1Waves] reported '{wave.taskId}' for {station.owner}; waiting for the " +
                      "other player.", this);
        }

        /// <summary>
        /// The server says both roles have reported. Raised on every machine, which is what
        /// makes the line land on both at once.
        /// </summary>
        void HandleUnlocked(string taskId)
        {
            for (int i = 0; i < m_Waves.Length; i++)
            {
                Wave wave = m_Waves[i];
                if (wave == null || wave.unlocked) continue;
                if (wave.taskId != taskId) continue;

                Unlock(i, "both players");
                return;
            }
        }

        void Unlock(int index, string why)
        {
            Wave wave = m_Waves[index];
            if (wave.unlocked) return;
            wave.unlocked = true;

            Debug.Log($"[O1Waves] {Label(wave, index)} unlocked ({why}).", this);
            Pump();
        }

        // ------------------------------------------------------------------ the objects

        /// <summary>
        /// The line that answered a wave has finished, so the NEXT wave's objects arrive -- for
        /// both players at once. 「兩個人都抓完燈後 vo跑完 外套才會出現」.
        ///
        /// NOT ONE PLAYER'S BOX, WHICH IS WHAT THIS USED TO BE. For a while a player's own drop
        /// brought in their own next object, so the two of them ran on separate clocks and a
        /// quick player could be holding the jacket while the lamp's line was still playing.
        /// The line is a thing the two of them listen to together, and what follows it has to
        /// arrive for them together too -- otherwise one of them is being spoken to about an
        /// object the other has already moved on from.
        ///
        /// WHICH MEANS THE BOXES NO LONGER DRIVE ANYTHING HERE. They are still the beat's gate
        /// -- O1DropSequence will not report until both are full -- but nothing in this chain
        /// waits on them, which is why <see cref="Side"/> no longer holds one.
        /// </summary>
        void BringIn(int index)
        {
            if (index >= m_Waves.Length) return;

            Wave wave = m_Waves[index];

            if (wave == null || wave.isPause)
            {
                // A pause has no objects at all, which is not a fault: it speaks for itself and
                // NextToSpeak picks it up.
                return;
            }

            int arriving = 0;

            foreach (Side side in wave.sides)
            {
                if (side == null) continue;

                if (side.appears == null)
                {
                    Debug.LogWarning($"[O1Waves] {Label(wave, index)} has nothing to fade in " +
                                     $"for {side.role}, so their object never arrives. Wire " +
                                     "that side's PropPresence.", this);
                    continue;
                }

                if (side.appears.presence >= 1f) continue;

                bool already = false;
                foreach (Arrival running in m_Arrivals)
                {
                    if (running.presence == side.appears) already = true;
                }
                if (already) continue;

                m_Arrivals.Add(new Arrival
                {
                    presence = side.appears,
                    since = ExperienceClock.now,
                    label = $"{Label(wave, index)} for {side.role}",
                });
                arriving++;
            }

            if (arriving > 0)
                Debug.Log($"[O1Waves] the line is over, so {Label(wave, index)} arrives for " +
                          $"{arriving} player(s) over {m_FadeSeconds:F1}s.", this);
        }

        void DriveArrivals()
        {
            for (int i = m_Arrivals.Count - 1; i >= 0; i--)
            {
                Arrival arrival = m_Arrivals[i];
                if (arrival.presence == null)
                {
                    m_Arrivals.RemoveAt(i);
                    continue;
                }

                float t = Mathf.Clamp01((ExperienceClock.now - arrival.since) / m_FadeSeconds);
                arrival.presence.presence = t;

                if (t < 1f) continue;

                Debug.Log($"[O1Waves] {arrival.label} is here.", this);
                m_Arrivals.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------ the lines

        /// <summary>
        /// Start the next line if the speaker is free. 「照順序播放完」 -- the queue IS the wave
        /// list, so a wave that unlocked early waits for the ones written before it.
        /// </summary>
        void Pump()
        {
            if (m_Speaking >= 0) return;

            int next = NextToSpeak();
            if (next >= 0) Speak(next);
        }

        /// <summary>
        /// The lowest wave that has not been heard yet AND is ready to be. Nothing jumps the
        /// queue: the first wave that is not ready stops the search, however many behind it are
        /// already unlocked.
        /// </summary>
        int NextToSpeak()
        {
            for (int i = 0; i < m_Waves.Length; i++)
            {
                Wave wave = m_Waves[i];
                if (wave == null) continue;
                if (wave.spoken) continue;

                // A pause is ready by definition: everything before it has been spoken, or the
                // loop would have stopped there.
                if (wave.isPause) return i;

                return wave.unlocked ? i : -1;
            }

            return -1;
        }

        void Speak(int index)
        {
            Wave wave = m_Waves[index];
            wave.spoken = true;
            m_Speaking = index;

            AudioClip mine = LineFor(wave);
            float longest = LongestLine(wave);

            if (longest <= 0f)
            {
                // Nothing to say, so nothing to wait for. A wave with no line is a wave the
                // writers have not got to yet, not a fault.
                Debug.LogWarning($"[O1Waves] {Label(wave, index)} has no line; going straight " +
                                 "on to the next one.", this);
                m_LineEndsAt = ExperienceClock.now;
                return;
            }

            if (m_Source != null && mine != null)
            {
                m_Source.clip = mine;
                m_Source.Play();
            }

            // THE CLOCK IS THE AUTHORITY, not AudioSource.isPlaying. The debug button unlocks a
            // wave without a speaker being audible at all, and a run with the audio device
            // muted still has to advance -- so what the beat waits on is the length of the clip
            // on the show's own clock, which both machines share.
            //
            // AND IT IS THE LONGEST CLIP, not this machine's. When the two players hear
            // different recordings they are rarely the same length, and the queue has to move
            // on for both of them on the same frame: timing it off whichever clip happens to be
            // playing here would start the next line early on one headset and late on the
            // other. The player whose line is shorter simply hears the end of it sooner.
            m_LineEndsAt = ExperienceClock.now + longest;

            Debug.Log($"[O1Waves] {Label(wave, index)}: playing " +
                      $"'{(mine != null ? mine.name : "nothing on this machine")}'; the queue " +
                      $"moves on in {longest:F1}s, which is the longer of the lines.", this);
        }

        /// <summary>
        /// The recording THIS machine plays. A shared line is the same for both; own lines are
        /// one per role, and with no role assigned -- a desk test -- the first of them, so the
        /// beat can still be walked alone.
        /// </summary>
        AudioClip LineFor(Wave wave)
        {
            if (wave.ownLines == null || wave.ownLines.Length == 0) return wave.line;

            PlayerRole local = InteractionDebug.LocalRole();
            foreach (RoleLine own in wave.ownLines)
            {
                if (own != null && own.role == local) return own.clip;
            }

            if (local != PlayerRole.Unassigned) return null;
            return wave.ownLines[0]?.clip;
        }

        /// <summary>The longest of a wave's recordings, which is what the queue is timed off.</summary>
        static float LongestLine(Wave wave)
        {
            float longest = wave.line != null ? wave.line.length : 0f;
            if (wave.ownLines == null) return longest;

            foreach (RoleLine own in wave.ownLines)
            {
                if (own?.clip != null) longest = Mathf.Max(longest, own.clip.length);
            }
            return longest;
        }

        void Update()
        {
            DriveArrivals();

            if (m_Speaking < 0) return;
            if (ExperienceClock.now < m_LineEndsAt + m_Gap) return;

            int finished = m_Speaking;
            m_Speaking = -1;
            m_LineEndsAt = -1f;

            Debug.Log($"[O1Waves] {Label(m_Waves[finished], finished)} is over.", this);

            // 「vo跑完 外套才會出現」: the objects of the wave AFTER the one just heard, for both
            // players on the same frame. Before Pump, so a pause wave that is ready to speak
            // does not get its line in ahead of the arrival it was waiting for.
            BringIn(finished + 1);

            Pump();

            if (m_Speaking < 0 && AllSpoken())
                Debug.Log("[O1Waves] every line has been heard; what is left of O-1 is the " +
                          "players filling their boxes.", this);
        }

        bool AllSpoken()
        {
            foreach (Wave wave in m_Waves)
            {
                if (wave != null && !wave.spoken) return false;
            }
            return true;
        }

        static void SetPresence(Wave wave, float value)
        {
            if (wave == null) return;
            foreach (Side side in wave.sides)
            {
                if (side?.appears != null) side.appears.presence = value;
            }
        }

        static Side SideFor(Wave wave, PlayerRole role)
        {
            if (wave == null) return null;
            foreach (Side side in wave.sides)
            {
                if (side != null && side.role == role) return side;
            }
            return null;
        }

        int IndexOf(O1GrabStation station)
        {
            for (int i = 0; i < m_Waves.Length; i++)
            {
                Wave wave = m_Waves[i];
                if (wave == null) continue;
                foreach (Side side in wave.sides)
                {
                    if (side != null && side.station == station) return i;
                }
            }
            return -1;
        }

        int IndexOf(Wave wave)
        {
            for (int i = 0; i < m_Waves.Length; i++)
            {
                if (m_Waves[i] == wave) return i;
            }
            return -1;
        }

        static string Label(Wave wave, int index) =>
            wave == null || string.IsNullOrWhiteSpace(wave.name) ? $"wave {index + 1}" : wave.name;

        #region Debug panel

        // One row for the chain, on this machine. The stations and the boxes keep their own rows
        // -- whether a particular lamp has been picked up or put away is a different question
        // from which line the beat is on.
        public string debugId => "o1.waves";

        // ASCII, because the panel's font has no CJK glyphs; see AvatarFormationSync.debugLabel.
        public string debugLabel => "O-1 -- both grab, a line, then the next object";

        public DebugInteractionScope debugScope => DebugInteractionScope.LocalMachine;

        public string debugFireLabel => "Unlock";

        public DebugInteractionState DebugState()
        {
            var state = DebugInteractionState.Idle;
            state.fired = finished;

            if (!isActiveAndEnabled)
            {
                state.detail = "inactive -- its beat is not running";
                return state;
            }

            if (m_Waves.Length == 0)
            {
                state.detail = "no waves wired";
                return state;
            }

            if (finished)
            {
                state.detail = $"all {m_Waves.Length} heard";
                return state;
            }

            string arriving = m_Arrivals.Count > 0 ? $", {m_Arrivals.Count} arriving" : "";

            if (m_Speaking >= 0)
            {
                state.detail = $"{m_Speaking + 1}/{m_Waves.Length} " +
                               $"{Label(m_Waves[m_Speaking], m_Speaking)}: speaking, " +
                               $"{Mathf.Max(m_LineEndsAt - ExperienceClock.now, 0f):0.0}s left" +
                               arriving;
                return state;
            }

            int waiting = -1;
            for (int i = 0; i < m_Waves.Length; i++)
            {
                if (m_Waves[i] != null && !m_Waves[i].spoken) { waiting = i; break; }
            }

            if (waiting < 0)
            {
                state.detail = "last line finishing" + arriving;
                return state;
            }

            Wave wave = m_Waves[waiting];

            // What this machine can see. The other player's grab is a fact the server holds, so
            // a row reading "1/2 held" on a two-machine run is not a fault -- it is this
            // machine having done its half and waiting.
            state.detail = $"{waiting + 1}/{m_Waves.Length} {Label(wave, waiting)}: " +
                           $"{wave.held}/{wave.wired} held here" +
                           (m_Reported.Contains(wave.taskId) ? ", waiting for the other player" : "") +
                           arriving;
            return state;
        }

        public bool DebugCanFire(DebugFireTarget target)
        {
            if (finished) return false;
            if (target != DebugFireTarget.Local) return false;
            if (!isActiveAndEnabled) return false;

            foreach (Wave wave in m_Waves)
            {
                if (wave != null && !wave.spoken) return true;
            }
            return false;
        }

        /// <summary>
        /// Unlock the next line that has not been heard, without waiting for either player to
        /// pick anything up. Staff rescue for a grab that will not register -- it does NOT mark
        /// the objects as held and does NOT bring in the next object, because the boxes still
        /// have to be filled and those rows must keep telling the truth about where things are.
        /// </summary>
        public void DebugFire(DebugFireTarget target)
        {
            if (!DebugCanFire(target)) return;

            for (int i = 0; i < m_Waves.Length; i++)
            {
                if (m_Waves[i] == null || m_Waves[i].spoken) continue;
                Unlock(i, "debug");
                return;
            }
        }

        public bool DebugCanReset()
        {
            if (m_Speaking >= 0 || m_Arrivals.Count > 0) return true;
            foreach (Wave wave in m_Waves)
            {
                if (wave != null && (wave.spoken || wave.unlocked)) return true;
            }
            return false;
        }

        public void DebugReset()
        {
            m_Speaking = -1;
            m_LineEndsAt = -1f;
            m_Arrivals.Clear();
            m_Reported.Clear();

            if (m_Source != null && m_Source.isPlaying) m_Source.Stop();

            foreach (Wave wave in m_Waves)
            {
                if (wave == null) continue;
                wave.unlocked = false;
                wave.spoken = false;
            }

            for (int i = 1; i < m_Waves.Length; i++) SetPresence(m_Waves[i], 0f);
        }

        #endregion
    }
}
