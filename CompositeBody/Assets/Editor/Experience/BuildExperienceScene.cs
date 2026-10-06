using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CompositeBody.Experience;
using CompositeBody.Multiplayer;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Builds the experience scene as a walkable spine: the <see cref="ExperienceDirector"/>
    /// plus one labelled placeholder marker per <see cref="StoryBeat"/>, and nothing else.
    ///
    /// The point of building it this way is that the beat chain, the two-player gates, the staff
    /// overrides and the replication of all three are the parts that can only be tested on two
    /// machines -- and they are also the parts that every piece of real content will sit on top
    /// of. Getting them walkable against markers first means each beat can later be filled in
    /// one at a time without the spine moving underneath.
    ///
    /// Overwrites the scene at <see cref="ScenePath"/>, including the template placeholder cube
    /// that was there before.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.BuildExperienceScene.Run
    /// </summary>
    public static class BuildExperienceScene
    {
        public const string ScenePath = "Assets/_Scenes/CompositeBody_Experience.unity";

        /// <summary>Where the markers sit: straight ahead of the spawn, at standing eye height.</summary>
        static readonly Vector3 k_MarkerPosition = new(0f, 1.55f, 2.2f);

        public static void Run()
        {
            Debug.Log("[Experience] Starting...");

            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null)
            {
                Debug.LogError("[Experience] RESULT: FAIL - shader 'Universal Render Pipeline/Unlit' not found.");
                return;
            }

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                Debug.LogError("[Experience] RESULT: FAIL - built-in LegacyRuntime font not available.");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("Experience");
            BuildDirector(root);
            if (!BuildSharedRig(root)) return;
            if (!BuildBeatContent(root, unlit, font)) return;
            ConfigureAmbient();

            Directory.CreateDirectory("Assets/_Scenes");
            EditorSceneManager.MarkAllScenesDirty();

            // First save: objects created from script have no persistent GlobalObjectId until
            // the scene exists on disk, so the hash pass below has nothing to derive one from.
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);

            // Then assign the NetworkObject hashes and save again. Without this the director
            // keeps GlobalObjectIdHash = 0, which NGO cannot match between host and client, so
            // the beat index never reaches a client and the show runs only on the host.
            RegenerateNetworkObjectHashes();
            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);

            EnsureInBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Experience] Saved scene to {ScenePath}");

            if (!Verify()) return;

            Debug.Log("[Experience] RESULT: PASS");
        }

        static void BuildDirector(GameObject parent)
        {
            var go = new GameObject("ExperienceDirector");
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<NetworkObject>();
            var director = go.AddComponent<ExperienceDirector>();

            BeatDefinition[] defs = ExperienceDirector.DefaultBeats();

            // Authored through SerializedObject so the canonical beat sheet is written into the
            // saved scene, rather than only existing in whatever this editor session holds.
            var so = new SerializedObject(director);
            var beats = so.FindProperty("m_Beats");
            beats.arraySize = defs.Length;
            for (int i = 0; i < defs.Length; i++)
            {
                var element = beats.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("beat").intValue = (int)defs[i].beat;
                element.FindPropertyRelative("gateTaskId").stringValue = defs[i].gateTaskId ?? string.Empty;
                element.FindPropertyRelative("autoAdvanceSeconds").floatValue = defs[i].autoAdvanceSeconds;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[Experience] Director authored with {defs.Length} beats.");
        }

        /// <summary>
        /// Things no single beat owns. <see cref="ScreenFade"/> lives here because the piece
        /// fades between beats, so it has to outlive whichever beat is running -- a fade object
        /// inside a beat's content root would be deactivated halfway through its own fade out.
        /// </summary>
        static bool BuildSharedRig(GameObject parent)
        {
            var fadeShader = Shader.Find("CompositeBody/ScreenFade");
            if (fadeShader == null)
            {
                Debug.LogError("[Experience] RESULT: FAIL - shader 'CompositeBody/ScreenFade' not found.");
                return false;
            }
            if (ShaderUtil.ShaderHasError(fadeShader))
            {
                foreach (var m in ShaderUtil.GetShaderMessages(fadeShader))
                    Debug.LogError($"[Experience] ScreenFade {m.severity} line {m.line}: {m.message}");
                Debug.LogError("[Experience] RESULT: FAIL - ScreenFade shader has compile errors.");
                return false;
            }

            var go = new GameObject("ScreenFade");
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<ScreenFade>();

            Debug.Log("[Experience] Shared ScreenFade added.");
            return true;
        }

        static bool BuildBeatContent(GameObject parent, Shader unlit, Font font)
        {
            var beatsRoot = new GameObject("Beats");
            beatsRoot.transform.SetParent(parent.transform, false);

            int built = 0;
            int placeholders = 0;

            foreach (var beat in StoryBeats.Ordered)
            {
                // The component sits on an always-active object and toggles the child, because a
                // BeatController on a deactivated object could never switch itself back on.
                var holder = new GameObject($"Beat_{StoryBeats.ShortCode(beat)}");
                holder.transform.SetParent(beatsRoot.transform, false);

                var content = new GameObject("Content");
                content.transform.SetParent(holder.transform, false);

                if (beat == StoryBeat.O0_Arrival)
                {
                    // Built beats sit at the origin and fill the room; only the placeholder
                    // markers need to be put somewhere the player will be looking.
                    if (!BuildO0Arrival.Populate(content))
                    {
                        Debug.LogError("[Experience] RESULT: FAIL - could not author O-0 content.");
                        return false;
                    }

                    var arrival = holder.AddComponent<O0ArrivalBeat>();
                    var arrivalSo = new SerializedObject(arrival);
                    arrivalSo.FindProperty("m_Beat").intValue = (int)beat;
                    arrivalSo.FindProperty("m_ContentRoot").objectReferenceValue = content;
                    arrivalSo.ApplyModifiedPropertiesWithoutUndo();

                    built++;
                }
                else
                {
                    // Rotated to face back toward the spawn point; a TextMesh reads along its own +z.
                    content.transform.SetPositionAndRotation(k_MarkerPosition, Quaternion.Euler(0f, 180f, 0f));

                    BuildBacking(content, unlit, beat);
                    TextMesh label = BuildLabel(content, font);

                    var placeholder = holder.AddComponent<PlaceholderBeat>();
                    var so = new SerializedObject(placeholder);
                    so.FindProperty("m_Beat").intValue = (int)beat;
                    so.FindProperty("m_ContentRoot").objectReferenceValue = content;
                    so.FindProperty("m_Synopsis").stringValue = Synopsis(beat);
                    so.FindProperty("m_Label").objectReferenceValue = label;
                    so.ApplyModifiedPropertiesWithoutUndo();

                    placeholders++;
                }

                content.SetActive(false);
            }

            Debug.Log($"[Experience] Beats: {built} built, {placeholders} still placeholders.");
            return true;
        }

        static void BuildBacking(GameObject parent, Shader unlit, StoryBeat beat)
        {
            var backing = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backing.name = "Backing";
            backing.transform.SetParent(parent.transform, false);
            backing.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            backing.transform.localScale = new Vector3(1.6f, 0.9f, 1f);

            // Nothing in this scene simulates, and a collider here would be grabbable.
            Object.DestroyImmediate(backing.GetComponent<Collider>());

            var mat = new Material(unlit) { name = $"Marker_{StoryBeats.ShortCode(beat)}" };
            mat.SetColor("_BaseColor", BackingColor(beat));
            backing.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static TextMesh BuildLabel(GameObject parent, Font font)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = Vector3.zero;

            var text = go.AddComponent<TextMesh>();
            text.font = font;
            text.fontSize = 48;
            text.characterSize = 0.022f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            text.text = "(beat)";

            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return text;
        }

        /// <summary>Onboarding reads cool and underwater; S0 warms up as the room and the bodies arrive.</summary>
        static Color BackingColor(StoryBeat beat) => beat switch
        {
            StoryBeat.O0_Arrival => new Color(0.06f, 0.12f, 0.18f),
            StoryBeat.O1_Control => new Color(0.08f, 0.14f, 0.22f),
            StoryBeat.O2_Grasp => new Color(0.10f, 0.16f, 0.24f),
            StoryBeat.O3_Communion => new Color(0.13f, 0.14f, 0.26f),
            StoryBeat.O4_OnboardingEnd => new Color(0.05f, 0.05f, 0.07f),
            StoryBeat.S0_1_SoundBeforeSpace => new Color(0.04f, 0.04f, 0.05f),
            StoryBeat.S0_2_TracesFormBody => new Color(0.18f, 0.12f, 0.14f),
            StoryBeat.S0_3_EmptyRoomForms => new Color(0.20f, 0.17f, 0.13f),
            StoryBeat.S0_4_TheyLeave => new Color(0.16f, 0.13f, 0.12f),
            _ => new Color(0.08f, 0.08f, 0.08f)
        };

        /// <summary>
        /// The script's description of each beat, condensed. ASCII because TextMesh draws with
        /// the built-in legacy font, which has no CJK glyphs; the script's own wording is kept
        /// on <see cref="StoryBeat"/> itself.
        /// </summary>
        static string Synopsis(StoryBeat beat) => beat switch
        {
            StoryBeat.O0_Arrival =>
                "Sea / water / frame void. No body, faint hands.\nThe other player is a blurred shape in their own colour.",
            StoryBeat.O1_Control =>
                "A lamp on the water trembles.\nMake a fist: it loses gravity, floats, spins, judders.",
            StoryBeat.O2_Grasp =>
                "Cup, keys, clothes, book. Your colour seeps out of ONE of them.\nHands pass through the rest. Grab, move, hand over.",
            StoryBeat.O3_Communion =>
                "Pale light zone between you. Close in: particles cross, colours mix.\nOrbit each other and the muffled voices resolve.",
            StoryBeat.O4_OnboardingEnd =>
                "Props dim, prompts fade, role colours stay.\nA key turns. Black.",
            StoryBeat.S0_1_SoundBeforeSpace =>
                "Full black. Key in lock, tap, laughter, an argument,\ntape, a box dragged, a door. Each from its own direction.",
            StoryBeat.S0_2_TracesFormBody =>
                "Each sound leaves particles where it happened. They drift in and\nbuild hands, arms, chest. Look down. A and B differ.",
            StoryBeat.S0_3_EmptyRoomForms =>
                "Walls, window, door frame, floor. Furniture already gone.\nDust, pale patches, pressure marks on the floor.",
            StoryBeat.S0_4_TheyLeave =>
                "The real couple carry out the last boxes. No words.\nYour hand passes through them. A last look back. The door.",
            StoryBeat.End =>
                "The written script ends here.\nStaff restart for the next audience.",
            _ => string.Empty
        };

        static void ConfigureAmbient()
        {
            // The scene loads additively on top of the lobby, which has the lighting; this is
            // only so opening the scene on its own is not pitch black.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.14f, 0.16f, 0.20f);
            RenderSettings.ambientEquatorColor = new Color(0.10f, 0.11f, 0.13f);
            RenderSettings.ambientGroundColor = new Color(0.04f, 0.04f, 0.05f);
            RenderSettings.fog = false;
        }

        /// <summary>
        /// GameSessionManager loads this scene by name through NGO, which only works for a scene
        /// registered in Build Settings. Appends rather than reordering, so the entry Main
        /// occupies as scene 0 is left alone.
        /// </summary>
        static void EnsureInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path != ScenePath) continue;

                if (!scenes[i].enabled)
                {
                    scenes[i] = new EditorBuildSettingsScene(ScenePath, true);
                    EditorBuildSettings.scenes = scenes.ToArray();
                    Debug.Log("[Experience] Re-enabled existing build settings entry.");
                }
                else
                {
                    Debug.Log("[Experience] Already registered in build settings.");
                }
                return;
            }

            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[Experience] Added to build settings.");
        }

        /// <summary>
        /// Invokes NetworkObject's internal OnValidate, which assigns GlobalObjectIdHash from
        /// the object's persistent GlobalObjectId. Unity runs it when a human edits the object
        /// in the Inspector, but not for objects created and saved entirely from batch mode.
        /// </summary>
        static void RegenerateNetworkObjectHashes()
        {
            var onValidate = typeof(NetworkObject).GetMethod(
                "OnValidate", BindingFlags.NonPublic | BindingFlags.Instance);

            if (onValidate == null)
            {
                Debug.LogError("[Experience] Could not find NetworkObject.OnValidate; hashes may stay 0.");
                return;
            }

            foreach (var netObj in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include))
            {
                onValidate.Invoke(netObj, null);
                EditorUtility.SetDirty(netObj);
            }
        }

        static uint ReadGlobalObjectIdHash(NetworkObject netObj)
        {
            var prop = new SerializedObject(netObj).FindProperty("GlobalObjectIdHash");
            return prop != null ? prop.uintValue : 0u;
        }

        /// <summary>
        /// Checks all five of O-0's layers actually landed. Each of these has a failure mode
        /// that is invisible from the outside: a sea with no mesh renders nothing, a sound bed
        /// with no clips plays silence, and a blob with no renderer simply never appears -- and
        /// all three look identical to "the beat has not started yet" from inside a headset.
        /// </summary>
        static bool VerifyO0()
        {
            var o0 = Object.FindAnyObjectByType<O0ArrivalBeat>();
            if (o0 == null)
            {
                Debug.LogError("[Experience] RESULT: FAIL - O-0 has no O0ArrivalBeat.");
                return false;
            }

            Transform content = o0.transform.childCount > 0 ? o0.transform.GetChild(0) : null;
            if (content == null)
            {
                Debug.LogError("[Experience] RESULT: FAIL - O-0 has no content root.");
                return false;
            }

            var sea = content.GetComponentInChildren<MeshFilter>(true);
            bool seaOk = false;
            foreach (var mf in content.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.name != "SeaSurface") continue;
                seaOk = mf.sharedMesh != null && mf.sharedMesh.vertexCount > 1000;
                sea = mf;
                break;
            }
            if (!seaOk)
            {
                Debug.LogError("[Experience] RESULT: FAIL - O-0 sea surface missing or too coarse to displace " +
                               $"(mesh={(sea != null ? sea.sharedMesh?.vertexCount.ToString() : "none")} verts).");
                return false;
            }
            Debug.Log($"[Experience] OK   O-0 sea ({sea.sharedMesh.vertexCount} verts)");

            var frames = content.GetComponentsInChildren<FrameDrift>(true);
            if (frames.Length == 0)
            {
                Debug.LogError("[Experience] RESULT: FAIL - O-0 has no drifting frames.");
                return false;
            }
            Debug.Log($"[Experience] OK   O-0 frames ({frames.Length})");

            var motes = content.GetComponentInChildren<ParticleSystem>(true);
            if (motes == null)
            {
                Debug.LogError("[Experience] RESULT: FAIL - O-0 has no motes.");
                return false;
            }
            Debug.Log("[Experience] OK   O-0 motes");

            var soundBed = content.GetComponentInChildren<DistantSoundBed>(true);
            if (soundBed == null)
            {
                Debug.LogError("[Experience] RESULT: FAIL - O-0 has no sound bed.");
                return false;
            }

            var soundSo = new SerializedObject(soundBed);
            if (soundSo.FindProperty("m_Bed").objectReferenceValue == null)
            {
                Debug.LogError("[Experience] RESULT: FAIL - O-0 sound bed has no looping ambience source.");
                return false;
            }

            int clipCount = soundSo.FindProperty("m_Clips").arraySize;
            int sourceCount = soundSo.FindProperty("m_Sources").arraySize;
            for (int i = 0; i < clipCount; i++)
            {
                if (soundSo.FindProperty("m_Clips").GetArrayElementAtIndex(i).objectReferenceValue == null)
                {
                    Debug.LogError($"[Experience] RESULT: FAIL - O-0 distant clip {i} is unassigned.");
                    return false;
                }
            }
            if (clipCount == 0 || sourceCount == 0)
            {
                Debug.LogError($"[Experience] RESULT: FAIL - O-0 sound bed has {clipCount} clips and {sourceCount} sources.");
                return false;
            }
            Debug.Log($"[Experience] OK   O-0 sound ({clipCount} clips, {sourceCount} sources)");

            var blob = content.GetComponentInChildren<RoleBlobPresenter>(true);
            if (blob == null)
            {
                Debug.LogError("[Experience] RESULT: FAIL - O-0 has no other-player blob.");
                return false;
            }
            if (new SerializedObject(blob).FindProperty("m_BlobRenderer").objectReferenceValue == null)
            {
                Debug.LogError("[Experience] RESULT: FAIL - O-0 blob has no renderer assigned.");
                return false;
            }
            Debug.Log("[Experience] OK   O-0 other-player blob");

            return true;
        }

        static bool Verify()
        {
            var director = Object.FindAnyObjectByType<ExperienceDirector>();
            if (director == null)
            {
                Debug.LogError("[Experience] RESULT: FAIL - no ExperienceDirector in the saved scene.");
                return false;
            }

            if (director.beatCount != StoryBeats.Ordered.Length)
            {
                Debug.LogError($"[Experience] RESULT: FAIL - director has {director.beatCount} beats, " +
                               $"expected {StoryBeats.Ordered.Length}.");
                return false;
            }

            // Exactly one controller per beat, no duplicates: two would both run, and none would
            // look from inside the headset like the show had stalled.
            var controllers = Object.FindObjectsByType<BeatController>(FindObjectsInactive.Include);
            foreach (var beat in StoryBeats.Ordered)
            {
                int found = 0;
                string kind = "?";
                foreach (var c in controllers)
                {
                    if (c.beat != beat) continue;
                    found++;
                    kind = c is PlaceholderBeat ? "placeholder" : c.GetType().Name;
                }

                if (found != 1)
                {
                    Debug.LogError($"[Experience] RESULT: FAIL - {StoryBeats.ShortCode(beat)} has {found} controllers, expected 1.");
                    return false;
                }

                Debug.Log($"[Experience] OK   {StoryBeats.DisplayName(beat)}  ({kind})");
            }

            if (!VerifyO0()) return false;

            if (Object.FindAnyObjectByType<ScreenFade>() == null)
            {
                Debug.LogError("[Experience] RESULT: FAIL - no ScreenFade; beats cannot fade and S0-1 cannot happen in the dark.");
                return false;
            }
            Debug.Log("[Experience] OK   ScreenFade present");

            foreach (var netObj in Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include))
            {
                uint hash = ReadGlobalObjectIdHash(netObj);
                if (hash == 0)
                {
                    Debug.LogError($"[Experience] RESULT: FAIL - NetworkObject '{netObj.name}' has GlobalObjectIdHash 0, " +
                                   "so it can never synchronize to a client.");
                    return false;
                }
                Debug.Log($"[Experience] OK   NetworkObject '{netObj.name}' hash={hash}");
            }

            bool registered = false;
            foreach (var entry in EditorBuildSettings.scenes)
            {
                if (entry.path == ScenePath && entry.enabled) registered = true;
            }

            if (!registered)
            {
                Debug.LogError("[Experience] RESULT: FAIL - scene is not an enabled build settings entry, " +
                               "so GameSessionManager cannot load it by name.");
                return false;
            }
            Debug.Log("[Experience] OK   registered in build settings");

            return true;
        }
    }
}
