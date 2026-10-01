using System.Collections;
using UnityEngine;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// Base for anything that is only alive during one <see cref="StoryBeat"/>. Subclasses get
    /// <see cref="OnEnterBeat"/>/<see cref="OnExitBeat"/> and can ignore the question of how the
    /// beat is being driven.
    ///
    /// Entirely local: each client derives what to show from the replicated beat index, the same
    /// way <see cref="LocalRolePresenter"/> derives visuals from the replicated role list. Beat
    /// content therefore costs no network traffic of its own, and a client that reconnects
    /// mid-show lands in the beat that is actually running instead of having missed its start.
    /// </summary>
    public abstract class BeatController : MonoBehaviour
    {
        [SerializeField, Tooltip("The beat this content belongs to.")]
        StoryBeat m_Beat = StoryBeat.None;

        [SerializeField, Tooltip("Activated for the duration of the beat and deactivated otherwise. Optional. Must be a child, never this same object -- a component on a deactivated object cannot switch itself back on.")]
        GameObject m_ContentRoot;

        public StoryBeat beat => m_Beat;

        /// <summary>True between <see cref="OnEnterBeat"/> and <see cref="OnExitBeat"/>.</summary>
        public bool isActiveBeat { get; private set; }

        protected virtual void OnEnable() => StartCoroutine(WaitAndBind());

        protected virtual void OnDisable()
        {
            StopAllCoroutines();
            if (ExperienceDirector.Instance != null)
                ExperienceDirector.Instance.onBeatChanged -= HandleBeatChanged;
        }

        void OnValidate()
        {
            if (m_ContentRoot == gameObject)
            {
                Debug.LogError($"[{GetType().Name}] '{name}' has itself as its content root. " +
                               "Deactivating it would stop this component from ever running again; " +
                               "put the beat's content in a child.", this);
                m_ContentRoot = null;
            }
        }

        IEnumerator WaitAndBind()
        {
            while (ExperienceDirector.Instance == null) yield return null;
            ExperienceDirector.Instance.onBeatChanged += HandleBeatChanged;
            Apply(ExperienceDirector.Instance.currentBeat);
        }

        void HandleBeatChanged(StoryBeat previous, StoryBeat current) => Apply(current);

        void Apply(StoryBeat current)
        {
            bool shouldRun = m_Beat != StoryBeat.None && current == m_Beat;

            if (m_ContentRoot != null && m_ContentRoot != gameObject)
                m_ContentRoot.SetActive(shouldRun);

            if (shouldRun == isActiveBeat) return;
            isActiveBeat = shouldRun;

            if (shouldRun) OnEnterBeat();
            else OnExitBeat();
        }

        /// <summary>The beat has just started on this client.</summary>
        protected virtual void OnEnterBeat() { }

        /// <summary>The beat has just ended on this client.</summary>
        protected virtual void OnExitBeat() { }
    }
}
