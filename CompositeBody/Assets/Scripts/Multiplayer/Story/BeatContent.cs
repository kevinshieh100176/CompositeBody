using UnityEngine;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// Content that is simply present for one beat, or for a span of them, and absent otherwise.
    ///
    /// <see cref="BeatController"/> already does the whole job -- it watches the replicated beat
    /// and switches its content root on and off -- but it is abstract, so until now the only way
    /// to put anything on a beat was to write a class for it. That is right for content with
    /// behaviour of its own and wrong for a pair of lights that only need to not be on yet, and
    /// the cost of the ceremony was that such things were left ungated: the onboarding's orbit
    /// lights sat inside a content root covering all four of its beats, and were therefore up
    /// from the first frame of the show.
    ///
    /// SEVERAL OBJECTS, NOT ONE ROOT. The base takes a single content root, which has to be a
    /// child, because a component on a deactivated object cannot switch itself back on. The two
    /// orbit lights are siblings under the object this would sit on, and wrapping them in
    /// another empty transform purely to satisfy that shape would be rearranging the scene to
    /// suit the code. So this takes a list instead, and this object stays active to drive it.
    ///
    /// The initial state is set in OnEnable rather than left to OnExitBeat, because that one
    /// fires on a CHANGE: at start the beat is not running and the controller does not consider
    /// that a change, so nothing would switch the lights off and they would stay exactly as
    /// authored -- which is the bug this is here to fix.
    /// </summary>
    public class BeatContent : BeatController
    {
        [SerializeField, Tooltip("Switched on for the duration of the beat and off otherwise. " +
                                 "These may be children of this object; the base's Content Root " +
                                 "field is for a single one and is still there if that suits " +
                                 "better.")]
        GameObject[] m_Objects = System.Array.Empty<GameObject>();

        protected override void OnEnable()
        {
            // Off first, then the base binds and turns them on if the beat is in fact running.
            Show(false);
            base.OnEnable();
        }

        protected override void OnEnterBeat() => Show(true);

        protected override void OnExitBeat() => Show(false);

        void Show(bool on)
        {
            foreach (GameObject target in m_Objects)
            {
                if (target != null && target != gameObject) target.SetActive(on);
            }
        }
    }
}
