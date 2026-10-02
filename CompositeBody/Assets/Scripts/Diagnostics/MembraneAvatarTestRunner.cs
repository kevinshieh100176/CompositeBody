using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using CompositeBody.Avatar.Skin;
using CompositeBody.Multiplayer;
using XRMultiplayer;

namespace CompositeBody.Diagnostics
{
    /// <summary>
    /// Starts a host in the membrane/combine sample scene and checks that the things being
    /// tested actually came up: the player avatar spawned with its membrane films, and the
    /// assembly pair spawned with everything the server needs to weld it.
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
            UnityEditor.EditorApplication.Exit(m_Failures.Count == 0 ? 0 : 1);
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

            Check(films.Count == 3, $"avatar carries 3 membrane films (found {films.Count})");

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
