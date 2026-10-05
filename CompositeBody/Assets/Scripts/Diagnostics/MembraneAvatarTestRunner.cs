using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using CompositeBody.Avatar.Body;
using CompositeBody.Avatar.Skin;
using CompositeBody.Multiplayer;
using XRMultiplayer;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// Starts a host in the membrane/combine sample scene and checks that the things being
    /// tested actually came up: the player avatar spawned as the Ch36 membrane figure, solved
    /// against its tracked poses, and the assembly pair spawned with everything the server needs
    /// to weld it.
    ///
    /// The membrane check exists because of a specific trap. The template switches avatar
    /// renderers on and off depending on whether a player is local or remote -- you are not
    /// supposed to see your own head from the inside -- and the films are new objects it knows
    /// nothing about. If they were parented under the renderers it toggles, they would be
    /// switched off with them; if they are siblings, they survive. Either way the result is only
    /// visible once a player has actually spawned, which is why this runs in play mode rather
    /// than being another check in the prefab builder.
    /// </summary>
    public class MembraneAvatarTestRunner : MonoBehaviour
    {
        [SerializeField] float m_StepTimeout = 25f;

        readonly List<string> m_Failures = new();
        int m_Checks;

        IEnumerator Start()
        {
            Log("Starting.");
            yield return RunTest();

            Log($"{m_Checks} checks, {m_Failures.Count} failed.");
            foreach (string failure in m_Failures) Debug.LogError($"[MembraneTest]   FAILED: {failure}");

            if (m_Failures.Count == 0) Debug.Log("[MembraneTest] RESULT: PASS");
            else Debug.LogError("[MembraneTest] RESULT: FAIL");

            yield return null;

#if UNITY_EDITOR
            // Exiting is for the batch run, where the status code is the result. In an editor
            // somebody is sitting in front of, the same exit would close their editor out from
            // under them, so that run just leaves play mode with the report in the console.
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(m_Failures.Count == 0 ? 0 : 1);
            else UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(m_Failures.Count == 0 ? 0 : 1);
#endif
        }

        IEnumerator RunTest()
        {
            if (NetworkManager.Singleton == null)
            {
                Fail("No NetworkManager in the scene.");
                yield break;
            }

            yield return WaitUntil(
                () => GameSessionManager.Instance != null && StoryProgressManager.Instance != null,
                "session managers to come up");

            if (!NetworkManager.Singleton.StartHost())
            {
                Fail("StartHost() returned false.");
                yield break;
            }

            yield return WaitUntil(() => NetworkManager.Singleton.IsServer && NetworkManager.Singleton.IsListening,
                "host to start");
            if (!Check(NetworkManager.Singleton.IsServer, "host is server")) yield break;

            // --- the avatar ---
            yield return WaitUntil(() => XRINetworkPlayer.LocalPlayer != null, "local player avatar to spawn");
            var player = XRINetworkPlayer.LocalPlayer;
            if (!Check(player != null, "local player avatar spawned")) yield break;

            // Give the template's own local/remote renderer pass a few frames to run before
            // looking at what is still switched on.
            for (int i = 0; i < 10; i++) yield return null;

            var films = new List<SkinnedMeshRenderer>();
            foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.name.EndsWith("_MembraneFilm")) films.Add(smr);
            }

            // Four: the head, the two tracked hands, and the Ch36 body.
            Check(films.Count == 4, $"avatar carries 4 membrane films (found {films.Count})");

            foreach (var film in films)
            {
                Check(film.sharedMesh != null && film.sharedMesh.vertexCount > 0,
                    $"{film.name} has geometry");

                Check(film.sharedMaterial != null && film.sharedMaterial.shader != null &&
                      film.sharedMaterial.shader.name == "CompositeBody/VacuumMembrane",
                    $"{film.name} uses the membrane shader");

                Check(film.rootBone != null && film.bones != null && film.bones.Length > 0,
                    $"{film.name} is bound to bones");

                Check(film.updateWhenOffscreen, $"{film.name} will not cull on bind-pose bounds");
            }

            // Each film has to be shown exactly when the renderer it wraps is shown. That is the
            // real invariant: the avatar hides its own head and hands for the local player --
            // you are not meant to see the inside of your own skull, and your hands come from
            // the local rig -- and a film that did not follow suit would hang in your face.
            //
            // This deliberately does not assert that any particular part is visible in a
            // single-machine run. Which parts are on depends on local-versus-remote, so pinning
            // it to a fixed expectation would be testing this run rather than the rule.
            foreach (var film in films)
            {
                var link = film.GetComponent<MembraneFilmLink>();
                if (!Check(link != null, $"{film.name} has a MembraneFilmLink")) continue;

                Renderer source = link.source;
                if (!Check(source != null, $"{film.name} is linked to the renderer it wraps")) continue;

                bool sourceShown = source.enabled && source.gameObject.activeInHierarchy;
                bool filmShown = film.enabled && film.gameObject.activeInHierarchy;

                Check(filmShown == sourceShown,
                    $"{film.name} matches its source ({(sourceShown ? "source shown" : "source hidden")}, " +
                    $"{(filmShown ? "film shown" : "film hidden")})");

                Log($"film {film.name}: film active={film.gameObject.activeInHierarchy} enabled={film.enabled} | " +
                    $"source '{source.name}' active={source.gameObject.activeInHierarchy} enabled={source.enabled}");
            }

            // --- the Ch36 body ---
            // The body is the avatar now, and it is solved rather than authored: nothing about it
            // is visible in the prefab beyond a figure standing in its bind pose. These checks are
            // about whether the solve actually ran against the three tracked poses, which cannot
            // be known until a player has spawned and a frame has passed.
            var rig = player.GetComponentInChildren<MembraneBodyRig>(true);
            if (Check(rig != null, "avatar carries a MembraneBodyRig"))
            {
                Check(rig.bonesResolved, "the body rig is wired to every bone it solves");
                Check(rig.chainsCaptured, "the body rig measured its bind pose");

                // The hands are drawn by the tracked hand visuals and by the local rig, so the
                // body's own hands have to be out of the way or the player wears two pairs.
                Check(rig.leftHandBone != null && rig.leftHandBone.localScale.x < 0.5f,
                    "the body's left hand is collapsed");
                Check(rig.rightHandBone != null && rig.rightHandBone.localScale.x < 0.5f,
                    "the body's right hand is collapsed");

                // The wrists are the seam that shows: the hand is a separate mesh, so a wrist
                // that did not land on the tracked hand reads as a hand floating off a forearm.
                CheckWrist(rig.leftHandBone, rig.leftHandTarget, "left");
                CheckWrist(rig.rightHandBone, rig.rightHandTarget, "right");

                Log($"body: ownerView={rig.ownerView} scale={rig.transform.localScale.x:0.000} " +
                    $"position={rig.transform.position}");
            }

            // Ch36 brought its own head, so the template's has to be off. Checked at runtime as
            // well as in the prefab builder, because the template switches avatar renderers on
            // for the local player -- a head re-enabled on spawn would put two heads in one place.
            foreach (var renderer in player.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name != "Head" && renderer.name != "HMD") continue;
                Check(!renderer.enabled, $"the template's '{renderer.name}' is off, so the body has one head");
            }

            // Nothing floats over the head. Checked at runtime because the crown is switched on
            // when a player spawns and again whenever the session host changes, so a prefab that
            // looks clean can still put one back.
            foreach (var renderer in player.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name != "Host_Crown") continue;
                Check(!renderer.enabled || !renderer.gameObject.activeInHierarchy,
                    "the host crown is not drawn");
            }

            foreach (var canvas in player.GetComponentsInChildren<Canvas>(true))
            {
                Check(!canvas.enabled || !canvas.gameObject.activeInHierarchy,
                    $"the '{canvas.name}' name tag is not drawn");

                // The voice particles are not UI, so switching the canvas off does not cover them.
                foreach (var renderer in canvas.GetComponentsInChildren<Renderer>(true))
                {
                    Check(!renderer.enabled || !renderer.gameObject.activeInHierarchy,
                        $"'{renderer.name}' under the name tag is not drawn");
                }
            }

            // --- the local rig's own hands ---
            // A separate prefab from the avatar, and the one that decides whether a player can
            // see their own hands: their avatar's hands are hidden for them, because the avatar
            // is what everyone else sees. Checked here because the avatar films above are a
            // different set of objects entirely, so passing those says nothing about these.
            var origin = Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (Check(origin != null, "XR Origin present"))
            {
                var rigFilms = new List<MembraneFilmLink>();
                foreach (var link in origin.GetComponentsInChildren<MembraneFilmLink>(true))
                    rigFilms.Add(link);

                Check(rigFilms.Count == 2, $"local rig has 2 hand films (found {rigFilms.Count})");

                foreach (var link in rigFilms)
                {
                    Renderer film = link.film;
                    Renderer source = link.source;

                    if (!Check(film != null && source != null, $"{link.name} is linked both ways")) continue;

                    Check(film.sharedMaterial != null && film.sharedMaterial.shader != null &&
                          film.sharedMaterial.shader.name == "CompositeBody/VacuumMembrane",
                        $"{link.name} uses the membrane shader");

                    // The invariant, not a fixed expectation: with no headset attached the rig's
                    // hand visuals are off, so asserting the film is visible would be asserting
                    // that this run has hand tracking.
                    bool sourceShown = source.enabled && source.gameObject.activeInHierarchy;
                    bool filmShown = film.enabled && film.gameObject.activeInHierarchy;
                    Check(filmShown == sourceShown,
                        $"{link.name} matches its source ({(sourceShown ? "source shown" : "source hidden")})");

                    Log($"rig film {link.name}: film active={film.gameObject.activeInHierarchy} " +
                        $"enabled={film.enabled} | source '{source.name}' " +
                        $"active={source.gameObject.activeInHierarchy} enabled={source.enabled}");
                }
            }

            // --- the assembly pair ---
            var halves = Object.FindObjectsByType<CompositeHalf>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Check(halves.Length == 2, $"two halves in the scene (found {halves.Length})");

            foreach (var half in halves)
            {
                Check(half.IsSpawned, $"{half.name} spawned as a NetworkObject");
                Check(!half.isAssembled, $"{half.name} starts unassembled");
            }

            // One player cannot assemble the pair. Checked here as well as in the spine test
            // because this scene is the one a tester will actually be holding a half in, and
            // a pair that welds itself on a single grab would look like the mechanic working.
            bool bothRolesPresent = false;
            foreach (var entry in GameSessionManager.Instance.playerRoles)
            {
                if (entry.role == PlayerRole.Player2 && entry.isConnected) bothRolesPresent = true;
            }
            Check(!bothRolesPresent, "only one role is connected in this single-machine run");

            yield return new WaitForSeconds(2f);

            foreach (var half in halves)
                Check(!half.isAssembled, $"{half.name} still unassembled with one player present");
        }

        /// <summary>
        /// A solved wrist sits on the hand it belongs to. The tolerance is generous on purpose:
        /// where the hands are in a headless run depends on the rig, and the failure this is
        /// looking for is an arm left in its bind pose -- which puts the wrist half a metre out
        /// sideways, not a centimetre or two off.
        /// </summary>
        void CheckWrist(Transform wrist, Transform target, string side)
        {
            if (!Check(wrist != null && target != null, $"the {side} arm has a wrist and a target")) return;

            float error = Vector3.Distance(wrist.position, target.position);
            Check(error < 0.25f, $"the {side} wrist is solved onto its tracked hand ({error * 100f:0.0}cm off)");
        }

        IEnumerator WaitUntil(System.Func<bool> condition, string what)
        {
            float started = Time.realtimeSinceStartup;
            while (!condition())
            {
                if (Time.realtimeSinceStartup - started > m_StepTimeout)
                {
                    Fail($"timed out after {m_StepTimeout:0.#}s waiting for {what}");
                    yield break;
                }
                yield return null;
            }
        }

        bool Check(bool condition, string description)
        {
            m_Checks++;
            if (condition) Log($"ok   {description}");
            else Fail(description);
            return condition;
        }

        void Fail(string description) => m_Failures.Add(description);

        static void Log(string message) => Debug.Log($"[MembraneTest] {message}");
    }
}
