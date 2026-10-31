using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using UnityEngine;
using XRMultiplayer;
using CompositeBody.Multiplayer;

namespace CompositeBody.Experience
{
    /// <summary>
    /// Streams the two players' poses, the show's clock and which scene is running out to
    /// TouchDesigner over UDP, as OSC.
    ///
    /// WHY UDP AND NOT TCP. This is a continuous stream of the present moment, and every packet is
    /// made obsolete by the next one. TCP would retransmit a lost head position and deliver it
    /// late and in order, which is the opposite of useful -- a stale pose held in a queue is worse
    /// than a dropped one -- and head-of-line blocking on a congested LAN would stall the sender
    /// inside the render loop. UDP drops and moves on, which for this data is the correct
    /// behaviour rather than a compromise.
    ///
    /// WHY OSC ON TOP OF IT. OSC *is* UDP; it is a payload format, not a different transport.
    /// Raw bytes would work, but TouchDesigner would then have to parse them in an OSC In DAT
    /// callback, in Python, once per packet. An OSC In CHOP turns these messages into named float
    /// channels with no scripting at all, and that CHOP is what the rest of a TD network wants to
    /// plug into. The cost is about twenty bytes of address text per value.
    ///
    /// WHY ONE BUNDLE PER FRAME. Everything here describes a single instant. Split across packets,
    /// TD can cook a frame holding a head position from this tick and a show time from the last,
    /// and the two drift apart exactly when something is moving fast. One bundle is one datagram,
    /// which either arrives whole or not at all.
    ///
    /// WHY THE HOST SENDS. The host has both players -- NGO replicates the remote one to it -- and
    /// it owns show time, which <see cref="ShowTimelineDriver"/> keeps in server-owned numbers for
    /// exactly this reason. If both headsets sent, TD would get two streams that disagree about
    /// the remote player by one network tick and would have to pick one. With no session at all
    /// this still sends, so a desk test reaches TD.
    ///
    /// Everything is in Unity world space, which in this piece is the room: the orbits circle
    /// (0,0,0) and the floor is y = 0, so TD receives coordinates already in stage metres.
    ///
    /// The destination can be overridden at launch with '-tdhost &lt;ip&gt;', which is what a build
    /// in a venue needs: the TD machine's address is not known until load-in.
    /// </summary>
    public class TouchDesignerSender : MonoBehaviour
    {
        [Header("Destination")]
        [SerializeField, Tooltip("TouchDesigner's machine. 127.0.0.1 when TD runs on this PC, " +
                                 "which also means Windows Firewall never sees the traffic. " +
                                 "'-tdhost <ip>' on the command line wins over this, which is " +
                                 "how a build reaches a machine nobody had the address of when " +
                                 "it was built.")]
        string m_Host = "127.0.0.1";

        [SerializeField, Tooltip("Must match the OSC In CHOP's port in TD.")]
        int m_Port = 10000;

        [SerializeField, Tooltip("Prefix on every address, so one TD network can take more than " +
                                 "one source without the channel names colliding.")]
        string m_Namespace = "/cb";

        [Header("Rate")]
        [SerializeField, Range(10f, 120f), Tooltip("Packets per second. Deliberately not the " +
                                                   "headset's 90: TD cooks at 60, and sending " +
                                                   "faster than it reads only costs bandwidth.")]
        float m_Rate = 60f;

        [Header("What to send")]
        [SerializeField] bool m_SendHands = true;

        [SerializeField, Tooltip("Which scene is running, taken from the show timeline's Control " +
                                 "tracks. '/beat/number' (1 = the first scene, 0 = none) goes " +
                                 "out in every packet; the label only when the scene changes.")]
        bool m_SendBeat = true;

        [SerializeField, Tooltip("Also send the beat's label ('O-0', 'S0-1') as an OSC string on " +
                                 "each change. That reaches an OSC In DAT, not a CHOP. Off if the " +
                                 "TD network only wants numbers.")]
        bool m_SendBeatCode = true;

        [SerializeField, Range(1, 10), Tooltip("How many consecutive packets carry the label " +
                                               "after a change. More than one because this is " +
                                               "UDP: a change sent once and dropped would leave " +
                                               "TD a scene behind for the rest of the show.")]
        int m_BeatCodeRepeats = 3;

        [SerializeField, Tooltip("Only the host sends. Off makes every headset send its own view, " +
                                 "which lowers latency for the local player but gives TD two " +
                                 "streams that disagree about the remote one.")]
        bool m_HostOnly = true;

        [Header("Coordinates")]
        [SerializeField, Tooltip("Unity is left-handed, TouchDesigner is right-handed, so Z and " +
                                 "two of the three euler angles flip. VERIFY AGAINST A KNOWN POSE " +
                                 "-- a handedness mismatch looks mirrored, which is plausible " +
                                 "enough to miss. Off sends Unity's own numbers untouched.")]
        bool m_ConvertToRightHanded = true;

        [Header("Diagnostics")]
        [SerializeField, Tooltip("Log the first packet's size and destination, then stay quiet.")]
        bool m_LogFirstPacket = true;

        // The most a UDP payload can carry over Ethernet without IP fragmentation: 1500 MTU less
        // 20 bytes of IP and 8 of UDP. Staying under it keeps "one bundle, one datagram" true --
        // a fragmented packet is lost entirely if any one fragment is, which would turn a small
        // link loss into a much larger one.
        const int k_MaxPayload = 1472;

        // OSC In CHOP names its channels after these addresses, which makes them the TD-side
        // interface: renaming one is a breaking change to the .toe, not an implementation detail.
        const string k_RoleA = "/a";
        const string k_RoleB = "/b";

        // WHY A COMMAND-LINE OVERRIDE AT ALL. The venue's TouchDesigner machine is not known
        // until load-in, and the address it gets there may not be the one it had the day before.
        // A build has no inspector, so without this the destination is whatever was serialised
        // when it was built, and changing one number would mean rebuilding on site. Follows the
        // '-solo' convention in GameSessionManager: a flag read off the command line, overriding
        // what the scene says. Unlike '-solo' it carries a value, so it consumes the next
        // argument.
        const string k_HostArgument = "-tdhost";

        // The namespace joined to each role, built once rather than per packet. Concatenating
        // inside the send loop would allocate two strings sixty times a second, which is the one
        // thing OscWriter exists to avoid -- and the players have to carry the namespace for the
        // same reason the clock does, or '/a/head/pos' collides with every other OSC source a TD
        // network happens to be listening to.
        string m_PrefixA = "/cb/a";
        string m_PrefixB = "/cb/b";

        readonly byte[] m_Packet = new byte[k_MaxPayload];
        readonly List<XRINetworkPlayer> m_Scratch = new();

        UdpClient m_Socket;
        IPEndPoint m_Target;
        float m_Accumulator;
        float m_NextRefresh;
        bool m_Logged;
        bool m_WarnedOverflow;

        // Set by <see cref="SetHost"/> while the piece is running, and deliberately not
        // serialised: a value typed into the staff panel under load-in pressure should not
        // silently outlive the session it was typed in, where it would quietly beat the field
        // someone had carefully set in the scene.
        string m_RuntimeHost;

        // What Open() actually resolved, which is not necessarily any of the three sources --
        // staff want to read back where the stream is pointed, not re-derive the precedence.
        string m_ActiveHost;

        /// <summary>Where the stream is pointed right now, whichever source won.</summary>
        public string activeHost => m_ActiveHost;

        /// <summary>The destination port, for a UI that has to tell staff what to open in TD.</summary>
        public int port => m_Port;

        XRINetworkPlayer m_PlayerA;
        XRINetworkPlayer m_PlayerB;

        int m_LastSceneNumber;
        bool m_BeatSeen;
        int m_BeatCodeLeft;

        void OnEnable() => Open();
        void OnDisable() => Close();

        /// <summary>Rebuilt here too, so retyping the namespace in the inspector takes effect.</summary>
        void OnValidate() => BuildPrefixes();

        void BuildPrefixes()
        {
            string ns = string.IsNullOrEmpty(m_Namespace) ? string.Empty : m_Namespace;
            m_PrefixA = ns + k_RoleA;
            m_PrefixB = ns + k_RoleB;
        }

        void Open()
        {
            Close();
            BuildPrefixes();

            string host = ResolveHost();
            m_ActiveHost = host;

            if (!IPAddress.TryParse(host, out IPAddress address))
            {
                // Resolved once, here, rather than per send: a DNS lookup on the hot path can
                // block for seconds when the name server is unreachable, and this runs inside the
                // render loop.
                try
                {
                    IPAddress[] found = Dns.GetHostAddresses(host);
                    address = Array.Find(found, a => a.AddressFamily == AddressFamily.InterNetwork);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[TouchDesigner] cannot resolve '{host}': {e.Message}", this);
                    return;
                }
            }

            if (address == null)
            {
                Debug.LogError($"[TouchDesigner] '{host}' has no IPv4 address.", this);
                return;
            }

            try
            {
                m_Socket = new UdpClient();

                // Non-blocking, so a full send buffer drops this frame's packet instead of
                // stalling the main thread. On a healthy link it never fills; the point is that
                // "never" and "not inside a 90 Hz render loop" are different standards.
                m_Socket.Client.Blocking = false;

                m_Target = new IPEndPoint(address, m_Port);
            }
            catch (Exception e)
            {
                Debug.LogError($"[TouchDesigner] cannot open the socket: {e.Message}", this);
                m_Socket = null;
            }
        }

        /// <summary>
        /// Staff panel, then command line, then the scene's own field.
        ///
        /// That order is the order of how recently a human said something: someone typing an
        /// address into the panel mid-show is correcting whatever the launch script and the scene
        /// believed, so they have to win. Each override logs, because "which machine am I pointed
        /// at" is the first question asked when TD shows nothing, and the answer should not
        /// require reading the launch script or opening the scene.
        /// </summary>
        string ResolveHost()
        {
            if (!string.IsNullOrEmpty(m_RuntimeHost))
            {
                Debug.Log($"[TouchDesigner] the staff panel overrides the host: streaming to " +
                          $"{m_RuntimeHost}.", this);
                return m_RuntimeHost;
            }

            string fromCommandLine = HostFromCommandLine();
            if (fromCommandLine != null)
            {
                Debug.Log($"[TouchDesigner] '{k_HostArgument}' overrides the scene: streaming to " +
                          $"{fromCommandLine} rather than {m_Host}.", this);
                return fromCommandLine;
            }

            return m_Host;
        }

        /// <summary>
        /// Repoints the stream while the piece is running, so the address can be set after the
        /// build has already started -- which is the normal case in a venue, where TD's IP is not
        /// known until load-in and sometimes not until someone is already wearing a headset.
        ///
        /// The socket is torn down and reopened rather than mutated: the endpoint is resolved once
        /// at open time precisely so no name lookup happens on the send path, and that only holds
        /// if changing the destination goes back through <see cref="Open"/>.
        ///
        /// Not persisted, by choice. See <see cref="m_RuntimeHost"/>. For an address that should
        /// survive a restart, launch with '-tdhost'.
        ///
        /// <paramref name="message"/> is written for a human either way, because the interesting
        /// failures here are all environmental -- a typo, a machine that is off, a name the venue's
        /// network cannot resolve -- and a silent false would send staff to the log.
        /// </summary>
        public bool SetHost(string host, out string message)
        {
            host = host?.Trim();

            if (string.IsNullOrEmpty(host))
            {
                message = "Enter TouchDesigner's IP or hostname first.";
                return false;
            }

            m_RuntimeHost = host;
            Open();

            if (m_Socket == null)
            {
                message = $"Could not reach '{host}' -- see the log for why.";
                return false;
            }

            // A socket on a disabled component is not a stream: Update never runs, so nothing is
            // ever sent. Saying "streaming" here would be the most misleading thing this panel
            // could tell someone who is waiting for channels to appear in TD.
            if (!isActiveAndEnabled)
            {
                message = $"Host set to {host}, but the sender is disabled -- nothing is sending.";
                return false;
            }

            message = $"Streaming to {m_Target} at {m_Rate:0} Hz.";
            return true;
        }

        /// <summary>
        /// The address after '-tdhost' on the command line, or null when the flag is absent.
        ///
        /// A missing value is reported rather than treated as an empty host: '-tdhost' typed with
        /// nothing after it, or with the next flag after it, is a typo, and falling back quietly
        /// to 127.0.0.1 would leave someone watching an OSC In CHOP that never fills while the
        /// log says everything is fine. An address never begins with '-', so that test is enough
        /// to tell a value from the next flag.
        /// </summary>
        static string HostFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], k_HostArgument, StringComparison.OrdinalIgnoreCase))
                    continue;

                string value = i + 1 < args.Length ? args[i + 1] : null;
                if (string.IsNullOrEmpty(value) || value[0] == '-')
                {
                    Debug.LogError($"[TouchDesigner] '{k_HostArgument}' needs an address after " +
                                   $"it, as '{k_HostArgument} 192.168.1.50'. Falling back to the " +
                                   "scene's own host.");
                    return null;
                }

                return value;
            }

            return null;
        }

        void Close()
        {
            m_Socket?.Close();
            m_Socket = null;
            m_Logged = false;
        }

        void Update()
        {
            if (m_Socket == null || !ShouldSend()) return;

            // A fixed interval rather than every frame, and the accumulator is clamped so a long
            // frame -- a scene load, a shader compile -- cannot queue a burst of back-dated
            // packets describing poses that are already wrong.
            float interval = 1f / Mathf.Max(m_Rate, 1f);
            m_Accumulator += Time.unscaledDeltaTime;
            if (m_Accumulator < interval) return;
            m_Accumulator = Mathf.Min(m_Accumulator - interval, interval);

            if (Time.unscaledTime >= m_NextRefresh)
            {
                m_NextRefresh = Time.unscaledTime + 0.5f;
                RefreshPlayers();
            }

            Send();
        }

        /// <summary>
        /// The host sends, and so does a machine with no session at all -- the same fallback the
        /// rest of the experience uses, so a desk test is not silent.
        /// </summary>
        bool ShouldSend()
        {
            if (!m_HostOnly) return true;

            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return true;
            return nm.IsServer;
        }

        /// <summary>
        /// Players are found by scan and cached, rechecked twice a second until both are known. A
        /// scan is not free, but it behaves the same on host and client and does not depend on
        /// which NGO spawn-manager API this version exposes, and twice a second for two objects
        /// is nothing beside getting it wrong when someone reconnects mid-show.
        /// </summary>
        void RefreshPlayers()
        {
            if (m_PlayerA != null && m_PlayerB != null) return;

            m_Scratch.Clear();
            m_Scratch.AddRange(FindObjectsByType<XRINetworkPlayer>());

            foreach (XRINetworkPlayer player in m_Scratch)
            {
                if (player == null) continue;
                switch (RoleOf(player))
                {
                    case PlayerRole.Player1: m_PlayerA = player; break;
                    case PlayerRole.Player2: m_PlayerB = player; break;
                }
            }
        }

        /// <summary>
        /// The session's own role table, so "a" here is the same player that is purple everywhere
        /// else. With no session the local player stands in as Player 1, which is what lets a
        /// single-machine test produce a stream TD can be built against.
        /// </summary>
        static PlayerRole RoleOf(XRINetworkPlayer player)
        {
            GameSessionManager session = GameSessionManager.Instance;
            if (session != null &&
                session.TryGetRoleForClient(player.OwnerClientId, out PlayerRole role) &&
                role != PlayerRole.Unassigned)
                return role;

            return player.IsLocalPlayer ? PlayerRole.Player1 : PlayerRole.Unassigned;
        }

        void Send()
        {
            var osc = new OscWriter(m_Packet);
            osc.BeginBundle();

            // --- the clock ------------------------------------------------------------------
            ShowTimelineDriver show = ShowTimelineDriver.Instance;
            float showTime = show != null ? show.showTime : 0f;
            bool held = show != null && show.waitingOn != null;

            osc.Message(m_Namespace, "/show/time", showTime);
            osc.Message(m_Namespace, "/show/held", held ? 1f : 0f);

            // The shared clock as well as show time. Show time stops at a gate and this does not,
            // so TD can keep ambient motion running while the running order waits for the players.
            osc.Message(m_Namespace, "/clock/now", ExperienceClock.now);
            osc.Message(m_Namespace, "/clock/shared", ExperienceClock.isShared ? 1f : 0f);

            // --- which scene is running -----------------------------------------------------
            if (m_SendBeat) WriteBeat(ref osc);

            // --- the players ----------------------------------------------------------------
            WritePlayer(ref osc, m_PrefixA, m_PlayerA);
            WritePlayer(ref osc, m_PrefixB, m_PlayerB);

            if (osc.overflowed)
            {
                if (!m_WarnedOverflow)
                {
                    m_WarnedOverflow = true;
                    Debug.LogError("[TouchDesigner] the bundle outgrew one datagram and was not " +
                                   "sent. Turn hands off, or split the send across two packets.",
                                   this);
                }
                return;
            }

            try
            {
                m_Socket.Send(m_Packet, osc.length, m_Target);

                if (m_LogFirstPacket && !m_Logged)
                {
                    m_Logged = true;
                    Debug.Log($"[TouchDesigner] streaming to {m_Target} -- {osc.length} bytes at " +
                              $"{m_Rate:0} Hz, addresses under '{m_Namespace}'.", this);
                }
            }
            catch (SocketException e)
            {
                // WouldBlock is the non-blocking socket saying its buffer is full. Dropping this
                // frame is the whole reason the socket is non-blocking, so it is not an error.
                if (e.SocketErrorCode != SocketError.WouldBlock)
                    Debug.LogWarning($"[TouchDesigner] send failed: {e.SocketErrorCode}", this);
            }
        }

        /// <summary>
        /// Which scene is running: one number, '/beat/number', in every packet, plus
        /// '/beat/code' when it changes.
        ///
        /// WHY THE NUMBER EVERY PACKET. An OSC In CHOP wants a channel that is simply always
        /// there, and it spots the change itself when the value moves. Sending it continuously
        /// also makes it idempotent: TD can start late, or drop packets, and still hold the right
        /// scene, which a change-only message cannot promise over UDP.
        ///
        /// Both come off the show timeline's Control tracks, which is where the running order
        /// actually lives -- see <see cref="ShowTimelineDriver.sceneNumber"/>. The number counts
        /// from 1, so O-0 is 1, and 0 means the clock is not inside any scene: before the first
        /// one, or past the end.
        ///
        /// WHY THE LABEL ONLY ON A CHANGE, AND THEN SEVERAL TIMES. The label is a string, so it
        /// lands in an OSC In DAT, and a DAT appends a row per message -- at 60 Hz the one row
        /// that matters would be buried in thousands. But a single datagram is exactly what UDP
        /// is allowed to lose, so it goes out in a few consecutive packets. The duplicate rows are
        /// identical and the number remains the authority; the repeat only protects the DAT's
        /// record of the change.
        /// </summary>
        void WriteBeat(ref OscWriter osc)
        {
            ShowTimelineDriver show = ShowTimelineDriver.Instance;
            int number = show != null ? show.sceneNumber : 0;

            osc.Message(m_Namespace, "/beat/number", number);

            // m_BeatSeen rather than comparing against 0, so the first packet after startup
            // announces the scene even when the show has not reached the first one yet.
            if (!m_BeatSeen || number != m_LastSceneNumber)
            {
                m_BeatSeen = true;
                m_LastSceneNumber = number;
                m_BeatCodeLeft = Mathf.Max(1, m_BeatCodeRepeats);
            }

            if (!m_SendBeatCode || m_BeatCodeLeft <= 0) return;

            m_BeatCodeLeft--;
            osc.Message(m_Namespace, "/beat/code", show != null ? show.sceneCode : string.Empty);
        }

        void WritePlayer(ref OscWriter osc, string prefix, XRINetworkPlayer player)
        {
            bool present = player != null && player.head != null;

            // Written every packet whether the player is there or not, so the channel always
            // exists in TD. A channel that appears and disappears changes the CHOP's shape
            // mid-cook, which downstream operators handle badly; a zero flag is something a
            // Select or a Logic can read.
            osc.Message(prefix, "/present", present ? 1f : 0f);

            if (!present)
            {
                osc.Message(prefix, "/head/pos", 0f, 0f, 0f);
                osc.Message(prefix, "/head/rot", 0f, 0f, 0f);
                if (m_SendHands)
                {
                    osc.Message(prefix, "/hand/l", 0f, 0f, 0f);
                    osc.Message(prefix, "/hand/r", 0f, 0f, 0f);
                }
                return;
            }

            Vector3 position = Convert(player.head.position);
            Vector3 rotation = ConvertEuler(player.head.eulerAngles);

            osc.Message(prefix, "/head/pos", position.x, position.y, position.z);
            osc.Message(prefix, "/head/rot", rotation.x, rotation.y, rotation.z);

            if (!m_SendHands) return;

            Vector3 left = player.leftHand != null ? Convert(player.leftHand.position) : Vector3.zero;
            Vector3 right = player.rightHand != null ? Convert(player.rightHand.position) : Vector3.zero;

            osc.Message(prefix, "/hand/l", left.x, left.y, left.z);
            osc.Message(prefix, "/hand/r", right.x, right.y, right.z);
        }

        /// <summary>
        /// Unity's left-handed Z becomes TouchDesigner's right-handed Z, which is a mirror in that
        /// one axis.
        /// </summary>
        Vector3 Convert(Vector3 v)
        {
            return m_ConvertToRightHanded ? new Vector3(v.x, v.y, -v.z) : v;
        }

        /// <summary>
        /// Mirroring one axis also reverses the sense of rotation about the other two, so X and Y
        /// negate while Z does not. Degrees either way, because TD's transforms take degrees.
        /// </summary>
        Vector3 ConvertEuler(Vector3 euler)
        {
            return m_ConvertToRightHanded ? new Vector3(-euler.x, -euler.y, euler.z) : euler;
        }

        /// <summary>
        /// Sends one packet now, from the inspector, with no headset and no play mode. The fastest
        /// way to find out whether TD is listening on the port you think it is.
        /// </summary>
        [ContextMenu("Send One Test Packet")]
        void SendTestPacket()
        {
            Open();
            if (m_Socket == null) return;

            m_Logged = false;
            RefreshPlayers();
            Send();

            if (!Application.isPlaying) Close();
        }
    }
}
