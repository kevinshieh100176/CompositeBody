using System.Collections.Generic;
using UnityEngine;
using XRMultiplayer;
using CompositeBody.Avatar.Skin;

namespace CompositeBody.Experience
{
    /// <summary>
    /// 「玩家沒有明確的身體，和真人觸碰後，才出現自己的雙手」 -- the player's own body arrives, and only
    /// once they have touched a 真人.
    ///
    /// Two layers, in order. The membrane grows first, using the same
    /// <see cref="MembraneReveal"/> that MembraneRoomAnimated drives, so the film forms outward
    /// from a point rather than fading up as a sheet. Only when it has finished does the Ch36
    /// body underneath come in -- which is the whole reason for the order: the membrane reads as
    /// something being made, and a body that appeared at the same time would read as something
    /// merely being switched on.
    ///
    /// THE BODY FADES THROUGH THE DITHER SHADER rather than through alpha. Ch36's materials are
    /// opaque Lit, and switching their surface mode at runtime to fade them would mean rewriting
    /// every one of them and sorting the result against the membrane. The project already ships
    /// SimpleLitDither variants of all of them, which XRAvatarVisuals swaps in for the local
    /// player so they do not see the inside of their own head -- that shader dithers by camera
    /// distance, so pushing its far threshold out hides the body and bringing it back in fades
    /// the body on. The fade is a property the shader already has, not one bolted on.
    ///
    /// Local only. This is what the player sees of themselves; the other player's view of them is
    /// their own business and is not touched here.
    /// </summary>
    public class AvatarFormation : MonoBehaviour
    {
        [SerializeField, Tooltip("Every film to grow -- body, both hands, head. Found in the " +
                                 "children if left empty.")]
        MembraneReveal[] m_Membranes = new MembraneReveal[0];

        [SerializeField, Tooltip("The Ch36 body renderers. Found in the children if left empty.")]
        Renderer[] m_BodyRenderers = new Renderer[0];

        [Header("Timing")]
        [SerializeField, Range(0f, 1f), Tooltip("THE WHOLE ARRIVAL, 0 to 1. Drag it to see any " +
                                                "moment of the formation without pressing Play. " +
                                                "In play mode it shows where the arrival has " +
                                                "got to; turn on Scrub below to drive it by hand " +
                                                "instead.")]
        float m_Progress;

        [SerializeField, Tooltip("Ignore the clock and use the slider above. For dialling the " +
                                 "curves in, in the editor, with the scene running or not.")]
        bool m_Scrub;

        [SerializeField, Min(0.1f), Tooltip("Seconds for the whole arrival -- the slider's 0 to " +
                                            "1. The curves below decide what happens when " +
                                            "inside it, so changing this stretches the shape " +
                                            "rather than rewriting it.")]
        float m_TotalSeconds = 32f;

        [SerializeField, Tooltip("Progress -> how far the film has grown. Finishing before 1 is " +
                                 "what leaves a gap before the body arrives.")]
        AnimationCurve m_MembraneCurve = new(new Keyframe(0f, 0f, 0f, 0f),
                                             new Keyframe(0.45f, 1f, 0f, 0f),
                                             new Keyframe(1f, 1f, 0f, 0f));

        [SerializeField, Tooltip("Progress -> how much of the Ch36 body is there. Starting after " +
                                 "the film has finished is what makes the body read as a " +
                                 "consequence of it rather than as a second thing switched on.")]
        AnimationCurve m_BodyCurve = new(new Keyframe(0f, 0f, 0f, 0f),
                                         new Keyframe(0.5f, 0f, 0f, 0f),
                                         new Keyframe(1f, 1f, 0f, 0f));

        [Header("Body fade")]
        [SerializeField, Tooltip("The dither shader's far threshold. Everything nearer than this " +
                                 "is dithered away, so a large value hides the body.")]
        string m_FadeProperty = "_MaxDistance";

        [SerializeField, Tooltip("Value that hides the body. Needs to exceed the distance from " +
                                 "the headset to the player's own feet.")]
        float m_HiddenDistance = 40f;

        [SerializeField, Min(0.05f), Tooltip("Roughly how far the body is from the headset -- " +
                                             "chest and thighs, not the feet. The dither " +
                                             "threshold is in metres and what it does to the " +
                                             "picture depends entirely on how far away the " +
                                             "surface is, so this is what makes the curve above " +
                                             "mean what it looks like it means.")]
        float m_NominalDistance = 0.9f;

        [SerializeField, Tooltip("Start with no body at all, however the avatar spawned. Later " +
                                 "beats want a body, so they call Restore.")]
        bool m_StartUnformed = true;

        /// <summary>The local player's own. Null until their avatar has spawned.</summary>
        public static AvatarFormation local { get; private set; }

        // Films that belong to the player but do not live under the avatar. The hands the player
        // actually sees are the XR hand visuals on the RIG, a different object tree that exists
        // before any avatar spawns and outlives it -- so they cannot be found by searching this
        // object's children, which is the whole reason this list exists.
        static readonly List<MembraneReveal> s_External = new();

        /// <summary>
        /// Add a film outside the avatar to the formation. Safe to call before any avatar
        /// exists; the film is brought to whatever state the formation is already in, so one
        /// registering late does not sit formed while the rest of the body is still arriving.
        /// </summary>
        public static void AddFilm(MembraneReveal film)
        {
            if (film == null || s_External.Contains(film)) return;
            s_External.Add(film);

            if (local == null) film.SetRevealImmediate(0f);
            else if (local.m_Formed) film.SetRevealImmediate(1f);
            else film.SetRevealImmediate(0f);
        }

        /// <summary>Drop a film, for a rig being torn down.</summary>
        public static void RemoveFilm(MembraneReveal film) => s_External.Remove(film);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_External.Clear();
            local = null;
        }


        int m_FadeId;
        readonly List<float> m_ShownValues = new();

        // Per renderer, not one flag for the set. The avatar mixes materials: the head and hands
        // are swapped onto the dither shader for the local player and have the fade property,
        // while Ch36's body material is plain URP/Lit and does not. A single flag meant one
        // fadeable material anywhere turned the property-block path on for ALL of them -- and a
        // renderer whose shader has no such property silently ignores the write, so the body
        // never hid at all.
        readonly List<bool> m_Fadeable = new();
        MaterialPropertyBlock m_Block;
        XRINetworkPlayer m_Player;

        /// <summary>Set only on someone else's avatar: the channel their touch arrives on.</summary>
        AvatarFormationSync m_RemoteSync;

        float m_StartTime;
        bool m_Running;
        bool m_Formed;
        bool m_CanFade;

        /// <summary>Whether the body has been asked to arrive.</summary>
        public bool formed => m_Formed;

        /// <summary>
        /// The body has arrived and the whole arrival has finished playing. What the next beat
        /// waits on: "formed" is true the instant the 真人 is touched, which is thirty seconds
        /// too early for anything that should follow the arrival rather than accompany it.
        /// </summary>
        public bool arrived => m_Formed && m_Progress >= 0.999f;

        void Awake()
        {
            m_FadeId = Shader.PropertyToID(m_FadeProperty);
            m_Block ??= new MaterialPropertyBlock();

            // Every film, not the first one found. The avatar carries four -- body, head and a
            // film per hand -- and driving only the body's left the hands already wrapped before
            // anyone had touched anything, which is the one thing the player can see of
            // themselves and so the one that had to be right.
            if (m_Membranes.Length == 0)
                m_Membranes = GetComponentsInChildren<MembraneReveal>(true);

            if (m_BodyRenderers.Length == 0)
            {
                var found = new List<Renderer>();
                foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    // The membrane films are driven by MembraneReveal and must not be dithered
                    // away underneath it.
                    if (r.GetComponentInParent<MembraneReveal>(true) != null) continue;
                    found.Add(r);
                }
                m_BodyRenderers = found.ToArray();
            }



            m_Player = GetComponent<XRINetworkPlayer>();
            if (m_Player != null)
            {
                m_Player.onSpawnedLocal += HandleSpawnedLocal;
                m_Player.onSpawnedAll += HandleSpawnedAll;
            }

            // Unformed from the first frame, before anyone knows whose avatar this is. The
            // network spawn callback can be a second or more after the object is alive, and a
            // second of fully-wrapped hands in front of the player's face is exactly the thing
            // this component exists to prevent. The other player's avatar is put back in
            // HandleSpawnedAll as soon as it is known not to be ours.
            if (m_StartUnformed) Unform();
        }

        void OnDestroy()
        {
            if (m_Player != null)
            {
                m_Player.onSpawnedLocal -= HandleSpawnedLocal;
                m_Player.onSpawnedAll -= HandleSpawnedAll;
            }
            if (local == this) local = null;
        }

        /// <summary>
        /// Fires on every avatar, ours included. For anyone else's, put the body back: the
        /// formation is about what a player sees of THEMSELVES, and a second person who arrives
        /// as an invisible nothing is a bug, not the piece.
        /// </summary>
        void HandleSpawnedAll()
        {
            if (m_Player != null && m_Player.IsOwner)
            {
                // Belt and braces. `local` is what the touch calls Form() through, and until it
                // is set the avatar sits unformed with no way out -- an invisible player who
                // cannot make themselves appear by touching anything. onSpawnedLocal normally
                // sets it, but it is one event on one code path, and the cost of it not firing
                // is the whole piece. This fires for every avatar, so owning one is a second,
                // independent way to find out that it is ours.
                if (local == null)
                {
                    Debug.LogWarning("[AvatarFormation] registering as the local avatar from " +
                                     "onSpawnedAll; onSpawnedLocal had not fired.");
                    HandleSpawnedLocal();
                }
                return;
            }

            FollowOwner();
        }

        /// <summary>
        /// Someone else's avatar. Start it unformed and let their replicated touch bring it in,
        /// so neither player sees the other as a person until that person has touched a 真人.
        ///
        /// THE OLD BEHAVIOUR WAS Restore(), AND IT WAS RIGHT AT THE TIME. The comment above it
        /// said a second person arriving as an invisible nothing is a bug, which was true while
        /// nothing replicated the touch: an avatar hidden here had no way of ever being told to
        /// appear. Now that there is a channel, hiding is recoverable -- so Restore stays as the
        /// fallback for when that channel is missing, rather than as the rule.
        /// </summary>
        void FollowOwner()
        {
            m_RemoteSync = AvatarFormationSync.For(m_Player);

            if (m_RemoteSync == null)
            {
                Debug.LogWarning("[AvatarFormation] a remote avatar has no AvatarFormationSync, " +
                                 "so nothing can ever tell it to form. Showing it whole rather " +
                                 "than leaving an invisible player in the room.", this);
                Restore();
                return;
            }

            if (m_RemoteSync.formed) FormAt(m_RemoteSync.formedAt);
            else Unform();
        }

        void HandleSpawnedLocal()
        {
            local = this;
            if (m_StartUnformed) Unform();
            Debug.Log($"[AvatarFormation] local avatar registered; " +
                      $"{m_BodyRenderers.Length} body renderer(s), " +
                      $"{m_Membranes.Length} membrane film(s) + {s_External.Count} on the rig.");
        }

        /// <summary>
        /// Remember what the materials were authored at, so the fade has somewhere to return to
        /// rather than a number guessed here.
        /// </summary>
        bool m_Captured;

        void CaptureShownValues()
        {
            if (m_Captured) return;
            m_Captured = true;

            m_ShownValues.Clear();
            m_Fadeable.Clear();
            m_CanFade = false;

            foreach (Renderer r in m_BodyRenderers)
            {
                float shown = 0f;
                bool fadeable = r != null && r.sharedMaterial != null &&
                                r.sharedMaterial.HasProperty(m_FadeId);
                if (fadeable)
                {
                    shown = r.sharedMaterial.GetFloat(m_FadeId);
                    m_CanFade = true;
                }
                m_ShownValues.Add(shown);
                m_Fadeable.Add(fadeable);
            }

            if (!m_CanFade)
                Debug.LogWarning($"[AvatarFormation] no body material has '{m_FadeProperty}', so " +
                                 "the body will appear rather than fade. That property comes from " +
                                 "the SimpleLitDither variants, which XRAvatarVisuals swaps in " +
                                 "for the local player -- if the swap has not run, this is why.");
        }

        /// <summary>No membrane, no body. The state O-0 opens in.</summary>
        public void Unform()
        {
            m_Formed = false;
            m_Running = false;
            SetProgress(0f);
        }

        /// <summary>
        /// Grow the membrane, then bring the body in behind it. Called when the player touches a
        /// 真人.
        /// </summary>
        public void Form()
        {
            if (m_Formed) return;

            FormAt(ExperienceClock.now);

            // Tell the other machine, so their copy of us grows too. Only the owner may send it,
            // and AvatarFormationSync ignores anyone else.
            if (m_Player != null && m_Player.IsOwner)
                AvatarFormationSync.For(m_Player)?.MarkFormed();

            Debug.Log($"[AvatarFormation] forming over {m_TotalSeconds}s.");
        }

        /// <summary>
        /// Form from a start time someone else decided -- the owner's, replicated here.
        ///
        /// The ramp is already written against <see cref="ExperienceClock"/> rather than against
        /// local time, so handing it a start stamp from another machine is all that replication
        /// needs: both then compute the same progress from the same clock. A packet that arrives
        /// late changes when the body starts appearing on this machine, not how grown it is --
        /// it simply picks up partway along, which is correct.
        /// </summary>
        public void FormAt(float startTime)
        {
            m_Formed = true;

            // Captured here, not in Awake: XRAvatarVisuals swaps the local player onto the
            // dither materials on the same spawn event this listens to, and the order of two
            // handlers on one Action is not defined. By the time anyone touches a 真人 the swap
            // has certainly happened -- so this recapture overrides anything an earlier Unform
            // or an inspector scrub read off the pre-swap materials.
            m_Captured = false;
            CaptureShownValues();

            m_StartTime = startTime;
            m_Running = true;

            float elapsed = (ExperienceClock.now - startTime) / Mathf.Max(m_TotalSeconds, 0.01f);
            SetProgress(Mathf.Clamp01(elapsed));
        }

        /// <summary>Everything on, now. For beats after O-0, which expect a body.</summary>
        public void Restore()
        {
            m_Formed = true;
            m_Running = false;
            CaptureShownValues();
            SetProgress(1f);
        }

        void LateUpdate()
        {
            // Polled rather than subscribed to the NetworkVariable's change event. Two reasons:
            // the value can already be set before this avatar spawns here, which an event alone
            // would miss entirely; and this project has been bitten once by a spawn callback that
            // did not fire -- the warning in HandleSpawnedAll exists because of it. Two field
            // reads a frame is not worth being clever about.
            if (m_RemoteSync != null && !m_Formed && m_RemoteSync.formed)
                FormAt(m_RemoteSync.formedAt);

            // The slider wins whenever it is asked to, including in play mode, so the curves can
            // be dialled in while the scene runs rather than by replaying the whole arrival
            // after every change.
            if (m_Scrub) { SetProgress(m_Progress); return; }
            if (!m_Running) return;

            float t = (ExperienceClock.now - m_StartTime) / Mathf.Max(m_TotalSeconds, 0.01f);
            SetProgress(t);

            if (t >= 1f)
            {
                m_Running = false;
                Debug.Log("[AvatarFormation] fully arrived.");
            }
        }

        // Scrubbing from the inspector outside play mode, so dragging the slider shows the
        // formation in the scene view without entering play.
        void OnValidate()
        {
            if (!m_Scrub || !isActiveAndEnabled) return;
            // Awake may not have run (a value changed on the prefab, or a domain reload), so
            // the property id is resolved here rather than assumed.
            m_Block ??= new MaterialPropertyBlock();
            m_FadeId = Shader.PropertyToID(m_FadeProperty);
            m_Captured = false;
            CaptureShownValues();
            SetProgress(m_Progress);
        }

        /// <summary>
        /// THE ONE PLACE the arrival is decided. Everything else -- the clock, the slider, a beat
        /// calling Restore -- comes through here, so what the inspector shows at a given progress
        /// is exactly what the show does at that progress.
        /// </summary>
        public void SetProgress(float progress)
        {
            m_Progress = Mathf.Clamp01(progress);

            float membrane = Mathf.Clamp01(m_MembraneCurve.Evaluate(m_Progress));
            float body = Mathf.Clamp01(m_BodyCurve.Evaluate(m_Progress));

            SetMembranes(membrane);
            SetBodyPresence(body);
        }

        /// <summary>Where the arrival has got to, 0 to 1.</summary>
        public float progress => m_Progress;

        /// <summary>Put every film at one value, now -- the avatar's and the rig's alike.</summary>
        void SetMembranes(float reveal)
        {
            foreach (MembraneReveal film in m_Membranes)
            {
                if (film != null) film.SetRevealImmediate(reveal);
            }
            foreach (MembraneReveal film in s_External)
            {
                if (film != null) film.SetRevealImmediate(reveal);
            }
        }

        /// <summary>
        /// 0 is no body at all, 1 is the body as authored.
        ///
        /// THE CURVE IS IN PICTURE, NOT IN METRES. The dither shader hides everything nearer
        /// than its threshold, so what a given threshold does to the image depends entirely on
        /// how far the surface is from the eye -- and sweeping the threshold linearly from 40 m
        /// to 0 spends nine tenths of its travel between 2% and 22% visible and then arrives in
        /// the last tenth. Sixteen seconds of that is fifteen seconds of nothing and a pop,
        /// which is exactly what it looked like.
        ///
        /// Inverting it is one line. A surface at distance d is visible in the fraction
        /// d / threshold, so asking for presence p at the nominal body distance means a
        /// threshold of d(1-p)/p: infinite at p = 0, equal to d at a half, and zero at 1. The
        /// authored curve then means what it says, and the shaping stays in the curve where it
        /// can be seen.
        /// </summary>
        void SetBodyPresence(float presence)
        {
            m_Block ??= new MaterialPropertyBlock();
            presence = Mathf.Clamp01(presence);

            for (int i = 0; i < m_BodyRenderers.Length; i++)
            {
                Renderer r = m_BodyRenderers[i];
                if (r == null) continue;

                // Without the dither property there is nothing to ramp, so this renderer is
                // simply switched. forceRenderingOff rather than enabled: enabled is not ours to
                // write -- every film carries a MembraneFilmLink that mirrors its source
                // renderer's enabled flag every LateUpdate, so switching a body renderer off
                // here switched its film off too, and a disabled SkinnedMeshRenderer stops
                // skinning, which is a body that no longer follows the player.
                if (i >= m_Fadeable.Count || !m_Fadeable[i])
                {
                    bool hide = presence < 0.5f;
                    if (r.forceRenderingOff != hide) r.forceRenderingOff = hide;
                    continue;
                }

                if (r.forceRenderingOff) r.forceRenderingOff = false;

                float shown = i < m_ShownValues.Count ? m_ShownValues[i] : 0f;
                float threshold = presence >= 0.999f
                    ? shown
                    : Mathf.Clamp(m_NominalDistance * (1f - presence) / Mathf.Max(presence, 1e-4f),
                                  shown, m_HiddenDistance);

                r.GetPropertyBlock(m_Block);
                m_Block.SetFloat(m_FadeId, threshold);
                r.SetPropertyBlock(m_Block);
            }
        }
    }
}
