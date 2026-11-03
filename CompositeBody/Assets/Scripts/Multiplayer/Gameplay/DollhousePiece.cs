using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using CompositeBody.Experience;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// One fragment of the dollhouse the players rebuild in S2-3: 「當把其他的碎片靠
    /// Lowpoly_Wall.Floor_small 時，那個碎片會吸上去然後對到正確的位置」.
    ///
    /// NOT A <see cref="CompositeHalf"/>, and the difference is the whole reason this exists.
    /// A pair of halves is two objects that find each other; this is many objects that each find
    /// the SAME fixed base, and each knows the one pose it belongs in. There is no partner to
    /// look up, no anchor to nominate, and no socket whose offset has to be derived from a cut --
    /// <see cref="m_Slot"/> is an empty child of the base, placed where this piece belongs, which
    /// is a thing somebody can drag in the scene view and see.
    ///
    /// ANYBODY MAY CARRY ANY PIECE. 「都能抓，先搶先贏」 -- so there is no <see cref="RoleGhost"/>
    /// here and no per-role ownership, which also means there is no role to validate a hold
    /// against. What stops a piece assembling itself is <see cref="wasEverHeld"/>: a fragment
    /// nobody has touched never snaps, however close to its slot it is parked. Without that, a
    /// corrected house whose authored scatter happens to put one piece near its own slot would
    /// assemble that piece before the beat had begun.
    ///
    /// THE DECISION IS THE SERVER'S, like every other one in this piece. Both headsets have to
    /// agree a fragment has landed, because what follows -- a fifteen-metre wall coming down out
    /// of the sky -- is not something one player can be shown and the other not. The server
    /// decides, writes <see cref="m_Placed"/>, and every machine freezes and un-grabs its own copy
    /// off the replicated flag. A headset that reconnects mid-beat reads the flag out of its spawn
    /// payload and finds the piece already in place rather than having missed the news.
    ///
    /// THE SERVER TAKES OWNERSHIP BACK BEFORE IT MOVES ANYTHING. Whoever is holding the fragment
    /// owns it, and with a client-authoritative transform anything the server writes to the pose is
    /// overwritten by the owner's next update -- the same trap <see cref="CompositeHalf.ServerAssemble"/>
    /// documents.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class DollhousePiece : NetworkBehaviour
    {
        [Header("Where it belongs")]
        [SerializeField, Tooltip("An empty child of the base, posed exactly where this fragment " +
                                 "belongs in the finished house. Drag it in the scene view to " +
                                 "move the slot -- nothing else needs changing.")]
        Transform m_Slot;

        [SerializeField, Min(0.005f), Tooltip("How close the fragment's origin has to come to the " +
                                              "slot before it is sucked in. Derived from the house's " +
                                              "own size by the builder rather than typed in metres, " +
                                              "for the reason BuildS2Fragments explains at length.")]
        float m_SnapRadius = 0.1f;

        [SerializeField, Min(0f), Tooltip("How long the suck itself takes. Zero teleports, which " +
                                          "reads as the piece having been taken away rather than " +
                                          "having gone home.")]
        float m_SnapTravel = 0.15f;

        [Header("References")]
        [SerializeField, Tooltip("Found on this object if left empty.")]
        XRGrabInteractable m_Interactable;

        [SerializeField, Tooltip("Found on this object if left empty.")]
        Rigidbody m_Rigidbody;

        readonly NetworkVariable<bool> m_Placed =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // LATCHED AND REPLICATED SEPARATELY, for the reason CompositeHalf.m_EverHeld gives: 「有人
        // 碰過」 is a different question from 「現在拿著」, the snap test asks the first one, and the
        // hand that answered it may have let go since.
        readonly NetworkVariable<bool> m_EverHeld =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Set the moment a hand closes on this machine, before the server has agreed. ON A DESK
        // WITH NO SESSION that is the only answer there will ever be, so a beat dialled in alone
        // still snaps.
        bool m_EverHeldLocally;

        bool m_PresentationApplied;

        // The glide into the slot, run by whoever owns the piece once it is placed.
        float m_Travel;
        float m_TravelStart = -1f;
        Vector3 m_TravelFrom;
        Quaternion m_TurnedFrom;

        /// <summary>Where this fragment belongs. Null on a piece the builder never finished.</summary>
        public Transform slot => m_Slot;

        /// <summary>True once the server has agreed this fragment is home.</summary>
        public bool placed => m_PresentationApplied || m_Placed.Value;

        /// <summary>True once anybody has had their hands on it, and never false again.</summary>
        public bool wasEverHeld => m_EverHeldLocally || m_EverHeld.Value;

        /// <summary>True while somebody is holding it.</summary>
        public bool held => m_Interactable != null && m_Interactable.isSelected;

        /// <summary>How close this fragment is to where it belongs, in metres.</summary>
        public float distanceToSlot =>
            m_Slot == null ? float.PositiveInfinity : Vector3.Distance(transform.position, m_Slot.position);

        /// <summary>How close it has to get. Read by the panel so a stalled beat reads as a number.</summary>
        public float snapRadius => m_SnapRadius;

        /// <summary>True while the piece is still sliding home.</summary>
        public bool arriving => m_TravelStart >= 0f;

        void Reset()
        {
            m_Interactable = GetComponent<XRGrabInteractable>();
            m_Rigidbody = GetComponent<Rigidbody>();
        }

        void Awake()
        {
            if (m_Interactable == null) m_Interactable = GetComponent<XRGrabInteractable>();
            if (m_Rigidbody == null) m_Rigidbody = GetComponent<Rigidbody>();

            if (m_Slot == null)
                Debug.LogWarning($"[Dollhouse] '{name}' has no slot, so it has nowhere to go and " +
                                 "will never snap. Re-run Build S2-3 Dollhouse.", this);
        }

        void OnEnable()
        {
            if (m_Interactable == null) return;
            m_Interactable.selectEntered.AddListener(HandleSelectEntered);
        }

        void OnDisable()
        {
            if (m_Interactable == null) return;
            m_Interactable.selectEntered.RemoveListener(HandleSelectEntered);
        }

        public override void OnNetworkSpawn()
        {
            m_Placed.OnValueChanged += HandlePlacedChanged;
            m_EverHeld.OnValueChanged += HandleEverHeldChanged;

            // A client that joins or reconnects after the piece landed receives the flag in its
            // spawn payload rather than as a change, so the freeze has to be applied here too.
            if (m_Placed.Value) ApplyPlacedPresentation();
        }

        public override void OnNetworkDespawn()
        {
            m_Placed.OnValueChanged -= HandlePlacedChanged;
            m_EverHeld.OnValueChanged -= HandleEverHeldChanged;
        }

        void HandleSelectEntered(SelectEnterEventArgs args)
        {
            // A socket holding a fragment is not a player holding it, and letting one count would
            // mean the house could build itself out of a rack with nobody in the room.
            if (args.interactorObject is XRSocketInteractor) return;

            m_EverHeldLocally = true;
            if (IsSpawned) ReportHeldRpc();
        }

        /// <summary>
        /// 「有人碰過這片了」. No role check, unlike <see cref="CompositeHalf.ReportHeldRpc"/> -- any
        /// player may carry any fragment here, so there is nothing a client could claim about this
        /// piece that it is not entitled to claim. What the flag buys is only that a fragment
        /// nobody has touched cannot snap.
        /// </summary>
        [Rpc(SendTo.Server)]
        void ReportHeldRpc() => m_EverHeld.Value = true;

        void HandleEverHeldChanged(bool previous, bool current)
        {
            if (current) m_EverHeldLocally = true;
        }

        void Update()
        {
            Glide();

            if (!IsSpawned || !IsServer || m_Placed.Value || m_Slot == null) return;
            if (!wasEverHeld) return;

            if ((transform.position - m_Slot.position).sqrMagnitude > m_SnapRadius * m_SnapRadius)
                return;

            ServerPlace(m_SnapTravel);
            Debug.Log($"[Dollhouse] '{name}' reached its slot and was sucked in.", this);
        }

        /// <summary>
        /// Put this fragment home, now. Used by the snap above and by
        /// <see cref="DollhouseAssembly"/> when the voice-over runs out and the rest of the house
        /// has to finish itself.
        ///
        /// The flag goes up at once rather than when the piece arrives: it IS placed from this
        /// moment -- out of both players' hands, frozen, counted -- and the glide is only how it
        /// gets there. Waiting for the arrival would leave a fragment somebody could still pull
        /// back out of the air mid-flight.
        /// </summary>
        public void ServerPlace(float travel)
        {
            if (!IsServer || m_Placed.Value || m_Slot == null) return;

            // Back to the server before anything moves, or the holder's next transform update
            // overwrites the glide.
            ulong serverId = NetworkManager.ServerClientId;
            if (NetworkObject.OwnerClientId != serverId) NetworkObject.ChangeOwnership(serverId);

            StartGlide(travel);
            m_Placed.Value = true;
        }

        /// <summary>
        /// Ask whichever machine is the server to place this fragment, from a machine that may
        /// not be it. What the debug panel's force button goes through: staff are as likely to be
        /// standing at a client as at the host, and a button that only worked on one of them
        /// would be found out during a stall.
        /// </summary>
        public void RequestPlace(float travel)
        {
            if (!IsSpawned)
            {
                // No session at all, so this machine is the only one there is.
                PlaceLocally();
                return;
            }

            if (IsServer) ServerPlace(travel);
            else ForcePlaceRpc(travel);
        }

        [Rpc(SendTo.Server)]
        void ForcePlaceRpc(float travel) => ServerPlace(travel);

        /// <summary>
        /// Puts the fragment back where it started so the beat can be rehearsed again. Server
        /// only, and the caller supplies the pose because where a fragment STARTS is the beat's
        /// business, not the fragment's -- see <see cref="DollhouseAssembly.DebugReset"/>.
        /// </summary>
        public void ServerUnplace(Vector3 position, Quaternion rotation)
        {
            if (!IsServer) return;

            m_TravelStart = -1f;
            m_Placed.Value = false;
            m_EverHeld.Value = false;
            m_EverHeldLocally = false;

            transform.SetPositionAndRotation(position, rotation);
            ClearPlacedPresentation();
        }

        /// <summary>
        /// Places the piece with no session running, for the scene builder and for dialling the
        /// beat in without standing up a host and two headsets. The same reason
        /// <see cref="CompositeHalf.AssembleLocally()"/> is public.
        /// </summary>
        public void PlaceLocally()
        {
            if (m_Slot == null) return;
            transform.SetPositionAndRotation(m_Slot.position, m_Slot.rotation);
            ApplyPlacedPresentation();
        }

        void StartGlide(float travel)
        {
            m_Travel = Mathf.Max(travel, 0f);
            if (m_Travel <= 0f)
            {
                transform.SetPositionAndRotation(m_Slot.position, m_Slot.rotation);
                m_TravelStart = -1f;
                return;
            }

            transform.GetPositionAndRotation(out m_TravelFrom, out m_TurnedFrom);
            m_TravelStart = ExperienceClock.now;
        }

        /// <summary>
        /// The suck itself, run only by the machine that owns the piece -- the server, which took
        /// ownership before it set the flag. Every other machine watches the replicated pose,
        /// which is also why this is not a coroutine: a client that joined mid-glide has no
        /// coroutine to be partway through, only a pose to read.
        /// </summary>
        void Glide()
        {
            if (m_TravelStart < 0f || m_Slot == null) return;
            if (IsSpawned && !IsOwner) { m_TravelStart = -1f; return; }

            float t = Mathf.Clamp01((ExperienceClock.now - m_TravelStart) / m_Travel);

            // Smoothstep, so it neither snatches itself out of the player's hand nor arrives at
            // speed.
            float eased = t * t * (3f - 2f * t);

            transform.SetPositionAndRotation(
                Vector3.LerpUnclamped(m_TravelFrom, m_Slot.position, eased),
                Quaternion.SlerpUnclamped(m_TurnedFrom, m_Slot.rotation, eased));

            if (t >= 1f) m_TravelStart = -1f;
        }

        void HandlePlacedChanged(bool previous, bool current)
        {
            if (current) ApplyPlacedPresentation();
            else ClearPlacedPresentation();
        }

        /// <summary>
        /// Everything a machine does once this fragment is home. Runs on every client including
        /// the host, because taking a thing out of a hand and switching off its collider are local
        /// concerns -- the server freezing only its own copy would leave the other player still
        /// carrying theirs.
        /// </summary>
        void ApplyPlacedPresentation()
        {
            if (m_PresentationApplied) return;
            m_PresentationApplied = true;

            // Out of the hand first. An interactor that is still selecting keeps writing the
            // transform and would drag the fragment straight back out of its slot.
            if (m_Interactable != null && m_Interactable.isSelected &&
                m_Interactable.interactionManager != null)
                m_Interactable.interactionManager.CancelInteractableSelection(
                    (IXRSelectInteractable)m_Interactable);

            if (m_Rigidbody != null) m_Rigidbody.isKinematic = true;
            if (m_Interactable != null) m_Interactable.enabled = false;
        }

        void ClearPlacedPresentation()
        {
            m_PresentationApplied = false;

            if (m_Rigidbody != null)
            {
                m_Rigidbody.isKinematic = false;
                m_Rigidbody.linearVelocity = Vector3.zero;
                m_Rigidbody.angularVelocity = Vector3.zero;
            }

            // Left disabled on purpose: whether a fragment may be picked up is the beat's
            // business, and DollhouseAssembly re-asserts it every frame from the beat root.
        }
    }
}
