using UnityEngine;

namespace CompositeBody.Experience
{
    /// <summary>
    /// O-0's sound layer: a low bed underneath everything, and occasional sounds from somewhere
    /// far off -- water, furniture being moved, a voice -- each arriving from its own direction
    /// and too muffled to make out. The script is explicit that the content cannot yet be
    /// understood, so these are deliberately filtered rather than merely quiet.
    ///
    /// Which clip plays, when, and from where is derived from <see cref="ExperienceClock"/>, so
    /// both headsets hear the same sound from the same direction at the same moment with nothing
    /// sent over the network. The alternative -- each machine rolling its own dice -- would have
    /// the two players in audibly different rooms while standing next to each other.
    /// </summary>
    public class DistantSoundBed : MonoBehaviour
    {
        [Header("Bed")]
        [SerializeField, Tooltip("Looping low ambience. Plays for as long as this object is active.")]
        AudioSource m_Bed;

        [Header("Distant events")]
        [SerializeField, Tooltip("One-shots picked from at random. Water, furniture, a voice.")]
        AudioClip[] m_Clips;

        [SerializeField, Tooltip("Pooled 3D sources the one-shots are played through. Three is enough for sounds this sparse.")]
        AudioSource[] m_Sources;

        [SerializeField, Min(1f), Tooltip("Seconds between distant events. The script asks for 'occasionally', not a texture.")]
        float m_Interval = 9f;

        [SerializeField, Min(0.5f)] float m_MinDistance = 6f;
        [SerializeField, Min(0.5f)] float m_MaxDistance = 14f;

        [SerializeField, Range(0f, 1f), Tooltip("Chance a given slot actually fires, so the rhythm is not metronomic.")]
        float m_FireChance = 0.72f;

        int m_LastSlot = int.MinValue;
        int m_NextSource;

        void OnEnable()
        {
            if (m_Bed != null && !m_Bed.isPlaying) m_Bed.Play();

            // Slots are absolute, so the first Update does not fire whichever slot happens to be
            // in progress when the beat starts -- that would be a sound with no lead-in.
            m_LastSlot = CurrentSlot();
        }

        void OnDisable()
        {
            if (m_Bed != null && m_Bed.isPlaying) m_Bed.Stop();

            if (m_Sources == null) return;
            foreach (var source in m_Sources)
            {
                if (source != null && source.isPlaying) source.Stop();
            }
        }

        void Update()
        {
            int slot = CurrentSlot();
            if (slot == m_LastSlot) return;

            m_LastSlot = slot;
            PlaySlot(slot);
        }

        int CurrentSlot() => Mathf.FloorToInt(ExperienceClock.now / Mathf.Max(0.01f, m_Interval));

        void PlaySlot(int slot)
        {
            if (m_Clips == null || m_Clips.Length == 0) return;
            if (m_Sources == null || m_Sources.Length == 0) return;

            // Three independent draws from one slot number. Both machines compute the same
            // three values because they are a pure function of the slot.
            float roll = Hash01(slot, 1);
            if (roll > m_FireChance) return;

            var clip = m_Clips[Mathf.Abs(HashInt(slot, 2)) % m_Clips.Length];
            if (clip == null) return;

            float bearing = Hash01(slot, 3) * Mathf.PI * 2f;
            float elevation = (Hash01(slot, 4) - 0.5f) * 0.45f;
            float distance = Mathf.Lerp(m_MinDistance, m_MaxDistance, Hash01(slot, 5));

            var direction = new Vector3(
                Mathf.Cos(bearing) * Mathf.Cos(elevation),
                Mathf.Sin(elevation),
                Mathf.Sin(bearing) * Mathf.Cos(elevation));

            var source = m_Sources[m_NextSource % m_Sources.Length];
            m_NextSource++;
            if (source == null) return;

            // Positioned in world space, not relative to the listener: a sound that followed the
            // player's head would read as being inside it rather than coming from the far side
            // of a room the two of them share.
            source.transform.position = transform.position + direction * distance;
            source.clip = clip;
            source.Play();
        }

        /// <summary>
        /// Deterministic hash of a slot and a stream index. Not statistically good, but it only
        /// has to be reproducible across two machines and varied enough that the pattern of
        /// distant sounds does not read as a loop.
        /// </summary>
        static int HashInt(int slot, int stream)
        {
            unchecked
            {
                int h = slot * 374761393 + stream * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                return h ^ (h >> 16);
            }
        }

        static float Hash01(int slot, int stream) => (Mathf.Abs(HashInt(slot, stream)) % 100000) / 100000f;
    }
}
