using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace CompositeBody.Interaction.Diagnostics
{
    /// <summary>
    /// A hand with no hardware behind it, for play-mode runners. Selecting through the real
    /// <see cref="XRInteractionManager"/> exercises the same path a tracked hand does -- select
    /// filters, dynamic attach, the grab's own transform writes -- so a runner that grabs with
    /// this is testing the interaction, not a mock of it. Same idea as O1PayoffTestRunner's
    /// private TestHand, made shareable.
    /// </summary>
    public class InteractionTestHand : XRBaseInteractor
    {
        /// <summary>Make a hand at <paramref name="position"/>, registered with the scene's manager.</summary>
        public static InteractionTestHand Create(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            return go.AddComponent<InteractionTestHand>();
        }

        /// <summary>
        /// Grab through the validated path (filters apply). Returns whether the select happened --
        /// false is how a runner sees a refusal. The hand is moved onto the object first, as a real
        /// hand would be.
        /// </summary>
        public bool TryGrab(XRGrabInteractable grab)
        {
            if (grab == null || interactionManager == null) return false;
            transform.position = grab.transform.position;
            interactionManager.SelectEnter((IXRSelectInteractor)this, (IXRSelectInteractable)grab);
            return grab.isSelected && hasSelection && firstInteractableSelected == (IXRSelectInteractable)grab;
        }

        /// <summary>Grab bypassing filters -- for setting up a state, never for testing a rule.</summary>
        public void ForceGrab(XRGrabInteractable grab)
        {
            if (grab == null || interactionManager == null) return;
            transform.position = grab.transform.position;
            interactionManager.SelectEnterUnconditionally((IXRSelectInteractor)this, (IXRSelectInteractable)grab);
        }

        /// <summary>Let go of whatever this hand holds.</summary>
        public void Release()
        {
            if (!hasSelection || interactionManager == null) return;
            interactionManager.SelectExit((IXRSelectInteractor)this, firstInteractableSelected);
        }

        /// <summary>Carry the held object (the grab follows the hand's attach transform).</summary>
        public void MoveTo(Vector3 position) => transform.position = position;
    }

    /// <summary>
    /// The bookkeeping every interaction runner repeats: named checks, a wait-with-timeout, a
    /// verdict line. Runners log "[Tag] RESULT: PASS/FAIL" and exit the editor ONLY in batch mode
    /// (see CLAUDE.md -- an unguarded exit closes the editor someone is sitting at).
    /// </summary>
    public sealed class InteractionTestLog
    {
        readonly string m_Tag;
        readonly List<string> m_Failures = new();
        int m_Passed;

        public InteractionTestLog(string tag) => m_Tag = tag;

        public int failures => m_Failures.Count;

        public bool Check(bool ok, string what)
        {
            if (ok)
            {
                m_Passed++;
                Debug.Log($"[{m_Tag}] OK   {what}");
            }
            else
            {
                m_Failures.Add(what);
                Debug.LogError($"[{m_Tag}] FAIL {what}");
            }
            return ok;
        }

        /// <summary>Yield until <paramref name="condition"/> or <paramref name="timeout"/>; records a check either way.</summary>
        public IEnumerator WaitFor(Func<bool> condition, float timeout, string what)
        {
            float start = Time.realtimeSinceStartup;
            while (!condition())
            {
                if (Time.realtimeSinceStartup - start > timeout)
                {
                    Check(false, $"{what} (timed out after {timeout:0.#}s)");
                    yield break;
                }
                yield return null;
            }
            Check(true, what);
        }

        /// <summary>Log the verdict; in batch mode, exit with it.</summary>
        public void Finish()
        {
            bool pass = m_Failures.Count == 0;
            string summary = pass
                ? $"[{m_Tag}] RESULT: PASS ({m_Passed} checks)"
                : $"[{m_Tag}] RESULT: FAIL ({m_Failures.Count} of {m_Passed + m_Failures.Count} failed: " +
                  string.Join("; ", m_Failures) + ")";
            if (pass) Debug.Log(summary); else Debug.LogError(summary);

#if UNITY_EDITOR
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(pass ? 0 : 1);
            else UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
