using System.Collections.Generic;
using System.Text;
using KWS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CompositeBody.Experience;

namespace CompositeBody.Multiplayer.EditorSetup
{
    /// <summary>
    /// Gives every section's water a profile asset of its own, and hands the switching to
    /// <see cref="LightingDirector"/>.
    ///
    /// THE PROBLEM THIS CLOSES. A KWS water keeps its look in a WaterSystemScriptableData, and
    /// nothing in this project had one as an asset -- every water carried an unnamed copy
    /// serialised into whichever scene it happened to be in. Three things follow from that, and
    /// all three had already happened:
    ///
    ///   The same prefab looks different in two scenes. s1_v4.prefab's River Water has no
    ///   Profile and no Settings of its own, so S1_v4.unity and MainScene each supplied one as a
    ///   prefab-instance override -- two unrelated objects. S1_v4.unity's was the authored river
    ///   (River mesh, spline, flow map, time scale 0.2). MainScene's was a default InfiniteOcean.
    ///
    ///   Editing one scene cannot fix the other. There is no shared asset to edit.
    ///
    ///   And the look gets overwritten by the neighbours. KWS copies the wind, waves, time scale
    ///   and underwater flags of WaterInstances[0] onto every other water -- and swaps an
    ///   InfiniteOcean to index 0 to make sure it is the one that wins. MainScene has four waters
    ///   in it. The onboarding ocean was overwriting the rest, and because Settings lives in the
    ///   scene file, saving the scene made it permanent.
    ///
    /// WHAT THIS DOES. Harvests each authored scene's water settings into a profile asset, points
    /// the water at it, re-points every embedded copy at a fresh clone of that profile, and fills
    /// in LightingDirector's water field per look so exactly one water is ever enabled.
    ///
    /// WHY THE PROFILE IS SET ON THE PREFAB. S1's and S2's waters live inside section prefabs.
    /// Setting Profile on the prefab asset rather than on the instance means a scene that
    /// instantiates it gets the right look without having to be told, which is the thing that was
    /// missing.
    ///
    /// WHY THE EMBEDDED COPY IS REWRITTEN RATHER THAN CLEARED. Clearing Settings would work --
    /// WaterSystem.LoadOrCreateSettings clones the Profile whenever Settings is null -- but only
    /// from the next scene load, and KWS reads Settings every frame in the meantime. Writing a
    /// clone of the profile in directly reaches the same state with no window where it is null.
    ///
    /// Re-runnable, and worth re-running: it is also how an edited profile gets pushed back into
    /// the scenes. Existing profile assets are updated in place rather than replaced, so the
    /// references to them survive.
    ///
    /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
    ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.SetUpWaterProfiles.Run
    /// </summary>
    public static class SetUpWaterProfiles
    {
        const string k_Main = "Assets/_Scenes/MainScene.unity";

        /// <summary>
        /// A scene whose water is the authored one, and the profile its look is harvested into.
        ///
        /// MainScene is deliberately not in this list. Its copies are the overwritten ones -- the
        /// river reads as a default ocean -- so it is on the receiving end of this and never the
        /// source.
        /// </summary>
        struct Source
        {
            public string scene;
            public string profile;
            public string prefab;   // where Profile is set, when the water lives in a prefab
        }

        static readonly Source[] k_Sources =
        {
            new Source
            {
                scene = "Assets/_Scenes/Onboarding.unity",
                profile = "Assets/_Scenes/WaterProfile_Onboarding.asset",
                prefab = null,      // the onboarding water belongs to the scene, not a prefab
            },
            new Source
            {
                scene = "Assets/_Scenes/IX_Scene/S1_v4.unity",
                profile = "Assets/_Scenes/IX_Scene/WaterProfile_S1River.asset",
                prefab = "Assets/_Scenes/IX_Scene/s1_v4.prefab",
            },
            new Source
            {
                scene = "Assets/_Scenes/IX_Scene/S2_v1.unity",
                profile = "Assets/_Scenes/IX_Scene/WaterProfile_S2.asset",
                prefab = "Assets/_Scenes/IX_Scene/S2_v1.prefab",
            },
        };

        /// <summary>Which look in MainScene gets which profile.</summary>
        static readonly Dictionary<string, string> k_LookProfiles = new Dictionary<string, string>
        {
            { "Onboarding", "Assets/_Scenes/WaterProfile_Onboarding.asset" },
            { "S1", "Assets/_Scenes/IX_Scene/WaterProfile_S1River.asset" },
            { "S2", "Assets/_Scenes/IX_Scene/WaterProfile_S2.asset" },
        };

        static int s_Fail;

        [MenuItem("Tools/Water/Set Up Water Profiles")]
        static void RunFromMenu()
        {
            // Every step below opens a scene Single, so an unsaved scene would be lost without
            // asking. Same guard the other tools in this folder use.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Run();
        }

        public static void Run()
        {
            Debug.Log("[Water] Starting...");
            s_Fail = 0;

            foreach (Source source in k_Sources) Harvest(source);
            ApplyToMainScene();

            Debug.Log(s_Fail == 0
                ? "[Water] RESULT: PASS"
                : $"[Water] RESULT: FAIL - {s_Fail} problem(s); see above.");
        }

        /// <summary>
        /// Save one scene's water settings as a profile, point the water at it, and leave the
        /// scene reading from a copy of that profile.
        /// </summary>
        static void Harvest(Source source)
        {
            Scene scene = EditorSceneManager.OpenScene(source.scene, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Fail($"could not open {source.scene}.");
                return;
            }

            WaterSystem[] waters = Object.FindObjectsByType<WaterSystem>(FindObjectsInactive.Include,
                                                                        FindObjectsSortMode.None);
            if (waters.Length != 1)
            {
                // Harvesting assumes the scene says unambiguously what its water looks like. More
                // than one and there is nothing here to pick between them.
                Fail($"{source.scene} has {waters.Length} water systems, expected 1.");
                return;
            }

            WaterSystem water = waters[0];
            if (water.Settings == null)
            {
                Fail($"{source.scene}: {water.name} has no Settings to harvest.");
                return;
            }

            WaterSystemScriptableData profile = SaveProfile(water.Settings, source.profile);
            if (profile == null) return;

            if (MakeWavesIndependent(profile))
                Debug.Log($"[Water] {profile.name}: moved onto its own wave simulation " +
                          $"(local wind {profile.LocalWindSpeed}, time {profile.LocalTimeScale}).");

            // The prefab is where Profile belongs, so that every scene instantiating the section
            // picks the look up without being told. For a scene-owned water there is no prefab
            // and the scene's own component holds it.
            if (source.prefab != null)
            {
                if (!SetProfileOnPrefab(source.prefab, profile)) return;
            }
            else
            {
                water.Profile = profile;
            }

            // Safe here: this scene was checked above for having exactly one water, so there is
            // nothing for the update to push onto.
            Reseat(water, profile, tellKws: true);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Water] {source.scene}: {Describe(water, profile)}");
        }

        /// <summary>
        /// Write <paramref name="settings"/> out as an asset, updating one that is already there
        /// rather than replacing it so nothing pointing at it comes unwired.
        /// </summary>
        static WaterSystemScriptableData SaveProfile(WaterSystemScriptableData settings, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<WaterSystemScriptableData>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(settings, existing);
                existing.name = System.IO.Path.GetFileNameWithoutExtension(path);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return existing;
            }

            var created = Object.Instantiate(settings);
            created.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();
            return created;
        }

        /// <summary>
        /// Move a water that is not the ocean onto its own wave simulation, carrying the authored
        /// numbers across. Returns whether anything changed.
        ///
        /// THIS IS WHAT MAKES A SECTION'S WAVES ITS OWN. Every <c>Global*</c> wave field is
        /// overwritten from <c>WaterInstances[0]</c>, and an InfiniteOcean is forced into that
        /// slot, so a river reading its waves from the global fields wears the ocean's waves
        /// whenever both are registered -- and the saved scene is what decides whether they are,
        /// which makes the river's look a consequence of how somebody left MainScene. The
        /// <c>Local*</c> fields are not in <c>CopySettings</c>, so a water reading from those
        /// keeps its own waves whatever else is up. KWS will not let an ocean do this
        /// (KWS_Editor forces UseGlobalWind on for InfiniteOcean), which is the right division:
        /// something has to own the shared simulation, and the ocean is the one that spans the
        /// whole world.
        ///
        /// The numbers are copied rather than reset, so this changes which simulation drives the
        /// river and not how the river looks.
        /// </summary>
        static bool MakeWavesIndependent(WaterSystemScriptableData profile)
        {
            if (profile.WaterMeshType == WaterSystemScriptableData.WaterMeshTypeEnum.InfiniteOcean)
                return false;
            if (!profile.UseGlobalWind) return false;

            profile.LocalWindSpeed       = profile.GlobalWindSpeed;
            profile.LocalWindRotation    = profile.GlobalWindRotation;
            profile.LocalWindTurbulence  = profile.GlobalWindTurbulence;
            profile.LocalFftWavesQuality = profile.GlobalFftWavesQuality;
            profile.LocalFftWavesCascades = profile.GlobalFftWavesCascades;
            profile.LocalWavesAreaScale  = profile.GlobalWavesAreaScale;
            profile.LocalTimeScale       = profile.GlobalTimeScale;
            profile.UseGlobalWind        = false;

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return true;
        }

        static bool SetProfileOnPrefab(string prefabPath, WaterSystemScriptableData profile)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                Fail($"could not open prefab {prefabPath}.");
                return false;
            }

            try
            {
                WaterSystem[] waters = root.GetComponentsInChildren<WaterSystem>(true);
                if (waters.Length != 1)
                {
                    Fail($"{prefabPath} has {waters.Length} water systems, expected 1.");
                    return false;
                }

                waters[0].Profile = profile;

                // Settings stays null ON THE PREFAB on purpose. A prefab that carried a Settings
                // object would hand every instance a shared copy to overwrite; left null, each
                // instance clones the profile for itself in Awake.
                waters[0].Settings = null;

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Replace whatever Settings the water is holding with a fresh copy of its profile.
        ///
        /// This is the step that repairs a scene: MainScene's river is holding a default ocean,
        /// and nothing short of overwriting it gets the authored river back, because KWS only
        /// clones the profile when Settings is null and this one is not.
        /// </summary>
        /// <param name="tellKws">
        /// Whether to make KWS pick the new settings up now. ONLY SAFE WITH ONE WATER
        /// REGISTERED. ForceUpdateWaterSettings does not just refresh this water -- it also
        /// pushes this water's wind, waves, time scale and underwater flags over every other
        /// registered water's settings. Call it while two are enabled and the second reseat
        /// undoes the first: the first version of this tool left the onboarding water holding
        /// S1's waves, which is the exact fault it was written to repair.
        /// </param>
        static void Reseat(WaterSystem water, WaterSystemScriptableData profile, bool tellKws)
        {
            var settings = Object.Instantiate(profile);

            // Named after the profile so the scene file stays legible. The copies that were in
            // there were called "(Clone)" and "(Clone)(Clone)", which is how far a copy of a copy
            // had already travelled from anything anyone chose.
            settings.name = profile.name;

            water.Settings = settings;
            if (tellKws) water.ForceUpdateWaterSettings();
            EditorUtility.SetDirty(water);
        }

        const string k_Reference = "Assets/_Scenes/IX_Scene/S1_v4.unity";

        /// <summary>
        /// Turn the river's spline to match the orientation MainScene places the section at, and
        /// bring the authoring scene into line so the two agree from then on.
        ///
        /// WHY THE SPLINE HAS TO MOVE AND THE WATER CANNOT. KWS pins a River's WORLD rotation to
        /// identity on every rebuild (<see cref="SectionTurnedUnderItsRiver"/>), so the channel
        /// is laid out along world axes whatever the section does. The river was drawn in
        /// S1_v4, where the section sits at yaw 0. MainScene places it at yaw 180: the model
        /// turned, the channel could not, and the river ended up running across the bed. The
        /// show's placement is the one that matters, so the spline is what moves.
        ///
        /// IDEMPOTENT BY CONSTRUCTION. The rotation applied is the DIFFERENCE between the two
        /// scenes' section yaws, and the run ends by setting the authoring scene to the show's
        /// yaw -- so a second run computes a difference of zero and does nothing. That is also
        /// why the authoring scene is moved rather than left alone: a difference that is never
        /// closed is a migration that runs again every time.
        ///
        /// The authoring scene's section is repositioned as well as turned, so that its water
        /// stays at the world position the spline is anchored to. Turning the root about its own
        /// pivot would otherwise drag the water out from under its own spline.
        ///
        /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
        ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.SetUpWaterProfiles.AlignSpline
        /// </summary>
        [MenuItem("Tools/Water/Align River Spline To The Show")]
        public static void AlignSpline()
        {
            Debug.Log("[Spline] Starting...");
            s_Fail = 0;

            if (!TryReadSection(k_Main, out float showYaw, out _, out _))             return;
            if (!TryReadSection(k_Reference, out float refYaw, out Vector3 waterWorld,
                                out Vector3 waterLocal))                              return;

            float delta = Mathf.DeltaAngle(refYaw, showYaw);
            if (Mathf.Abs(delta) < 0.5f)
            {
                Debug.Log($"[Spline] nothing to do: both scenes place the section at yaw " +
                          $"{showYaw:F0}.");
                Debug.Log("[Spline] RESULT: PASS");
                return;
            }

            var profile = AssetDatabase.LoadAssetAtPath<WaterSystemScriptableData>(
                "Assets/_Scenes/IX_Scene/WaterProfile_S1River.asset");
            if (profile == null || profile.SplineScriptableData == null)
            {
                Fail("the river profile has no spline data to turn.");
                return;
            }

            int moved = RotateSpline(profile.SplineScriptableData, delta);
            Debug.Log($"[Spline] turned {moved} point(s) by {delta:F0} degrees in " +
                      $"{profile.SplineScriptableData.name}, so the channel matches the " +
                      $"section at yaw {showYaw:F0}.");

            // And the authoring scene follows the show, which is what stops this from being a
            // migration that has to be run again.
            if (PlaceSection(k_Reference, showYaw, waterWorld, waterLocal))
                Debug.Log($"[Spline] {k_Reference}: section turned to yaw {showYaw:F0}, water " +
                          $"kept at {waterWorld}.");

            Debug.Log(s_Fail == 0 ? "[Spline] RESULT: PASS"
                                  : $"[Spline] RESULT: FAIL - {s_Fail} problem(s).");
        }

        /// <summary>Read a scene's river section without changing it.</summary>
        static bool TryReadSection(string scenePath, out float yaw, out Vector3 waterWorld,
                                   out Vector3 waterLocal)
        {
            yaw = 0; waterWorld = Vector3.zero; waterLocal = Vector3.zero;

            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (!scene.IsValid()) { Fail($"could not open {scenePath}."); return false; }

            WaterSystem river = FindRiver();
            if (river == null) { Fail($"{scenePath} has no River water."); return false; }
            if (river.transform.parent == null)
            {
                Fail($"{scenePath}: the river is not inside a section.");
                return false;
            }

            yaw        = river.transform.parent.eulerAngles.y;
            waterWorld = river.transform.position;
            waterLocal = river.transform.localPosition;
            return true;
        }

        /// <summary>
        /// Turn every spline point about the spline's own anchor. The points are stored as
        /// absolute world positions anchored at InitialSplinePosition, so that anchor is the
        /// pivot and stays put.
        /// </summary>
        static int RotateSpline(SplineScriptableData spline, float degrees)
        {
            Quaternion rot = Quaternion.Euler(0f, degrees, 0f);
            int count = 0;

            foreach (SplineScriptableData.Spline s in spline.Splines)
            {
                Vector3 anchor = s.InitialSplinePosition;
                foreach (SplineScriptableData.SplinePoint p in s.SplinePoints)
                {
                    p.WorldPosition = anchor + rot * (p.WorldPosition - anchor);
                    count++;
                }
            }

            EditorUtility.SetDirty(spline);
            AssetDatabase.SaveAssets();
            return count;
        }

        /// <summary>
        /// Turn a scene's section to <paramref name="yaw"/> while keeping its water at
        /// <paramref name="waterWorld"/>, so the water stays anchored to the spline.
        /// </summary>
        static bool PlaceSection(string scenePath, float yaw, Vector3 waterWorld, Vector3 waterLocal)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (!scene.IsValid()) { Fail($"could not reopen {scenePath}."); return false; }

            WaterSystem river = FindRiver();
            if (river == null) { Fail($"{scenePath}: the river went missing."); return false; }

            Transform section = river.transform.parent;
            var rotation = Quaternion.Euler(0f, yaw, 0f);

            section.rotation = rotation;
            section.position = waterWorld - rotation * waterLocal;

            // m_LocalEulerAnglesHint is what the Transform inspector SHOWS, and assigning
            // rotation does not touch it -- leaving the field reading 0 next to a quaternion
            // that is a half turn. Harmless to Unity, but the next person to read it is being
            // told the section is not rotated when the whole problem here was that it is.
            var sectionSo = new SerializedObject(section);
            sectionSo.FindProperty("m_LocalEulerAnglesHint").vector3Value = section.localEulerAngles;
            sectionSo.ApplyModifiedPropertiesWithoutUndo();

            river.ForceUpdateWaterSettings();
            EditorUtility.SetDirty(section);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return true;
        }

        static WaterSystem FindRiver()
        {
            foreach (WaterSystem w in Object.FindObjectsByType<WaterSystem>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (w.Settings != null
                    && w.Settings.WaterMeshType == WaterSystemScriptableData.WaterMeshTypeEnum.River)
                    return w;
            return null;
        }

        /// <summary>
        /// Check that both scenes place the river's section at the same yaw.
        ///
        /// WHY THIS IS THE INVARIANT, rather than "the section must not be rotated". KWS pins a
        /// River's WORLD rotation to identity on every rebuild, so the channel is laid out along
        /// world axes and cannot turn with its section. That is fine as long as the spline was
        /// drawn for the orientation the section is actually placed at -- and it is drawn once,
        /// in the authoring scene, for a section that the show then places its own way. So the
        /// thing that has to hold is that the two placements agree. When they do not, the model
        /// has turned and the channel has not, and the river runs across the bed.
        ///
        /// Checked across scenes because neither scene can see it alone: each one looks
        /// perfectly consistent with itself.
        /// </summary>
        static void CheckSectionsAgree()
        {
            if (!TryReadSection(k_Main, out float showYaw, out _, out _)) return;
            if (!TryReadSection(k_Reference, out float refYaw, out _, out _)) return;

            float delta = Mathf.DeltaAngle(refYaw, showYaw);
            if (Mathf.Abs(delta) < 0.5f)
            {
                Debug.Log($"[VerifyWater] ok   both scenes place the section at yaw {showYaw:F0}");
                return;
            }

            Fail($"the river's section is at yaw {showYaw:F0} in {k_Main} but yaw {refYaw:F0} in " +
                 $"{k_Reference}, a difference of {delta:F0}. A River is pinned to world rotation, " +
                 $"so the channel is drawn for one of those and runs across the bed in the other. " +
                 $"Tools > Water > Align River Spline To The Show turns the spline to match " +
                 $"{k_Main} and brings the other scene into line.");
        }

        /// <summary>
        /// Point MainScene's waters at the harvested profiles and give LightingDirector the
        /// switching.
        /// </summary>
        static void ApplyToMainScene()
        {
            Scene scene = EditorSceneManager.OpenScene(k_Main, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Fail($"could not open {k_Main}.");
                return;
            }

            var director = Object.FindFirstObjectByType<LightingDirector>();
            if (director == null)
            {
                Fail($"{k_Main} has no LightingDirector; nothing would switch the water.");
                return;
            }

            WaterSystem[] waters = Object.FindObjectsByType<WaterSystem>(FindObjectsInactive.Include,
                                                                         FindObjectsSortMode.None);
            var claimed = new HashSet<WaterSystem>();
            var report = new StringBuilder($"[Water] {k_Main}: {waters.Length} water system(s)");

            // EVERYTHING OFF BEFORE ANYTHING IS WIRED. KWS pushes a water's shared settings over
            // all the other registered waters, so reseating them one by one with the rest still
            // enabled leaves each one wearing whichever was reseated last. Emptying the instance
            // list first means every reseat below stands on its own, and the one water that comes
            // back up at the end comes up alone.
            foreach (WaterSystem water in waters) water.enabled = false;

            WaterSystem startWater = null;

            SerializedObject so = new SerializedObject(director);
            SerializedProperty looks = so.FindProperty("m_Looks");

            for (int i = 0; i < looks.arraySize; i++)
            {
                SerializedProperty look = looks.GetArrayElementAtIndex(i);
                string name = look.FindPropertyRelative("name").stringValue;

                if (!k_LookProfiles.TryGetValue(name, out string profilePath)) continue;

                var profile = AssetDatabase.LoadAssetAtPath<WaterSystemScriptableData>(profilePath);
                if (profile == null)
                {
                    Fail($"look '{name}': profile {profilePath} is missing.");
                    continue;
                }

                WaterSystem water = FindWaterFor(look, waters, claimed);
                if (water == null)
                {
                    Fail($"look '{name}': no water found for it in {k_Main}.");
                    continue;
                }

                claimed.Add(water);
                look.FindPropertyRelative("water").objectReferenceValue = water;

                // The scene's own water holds its Profile directly; a prefab's water already got
                // it from the prefab, and overwriting it here would only add an override saying
                // the same thing.
                if (water.Profile != profile && PrefabUtility.GetCorrespondingObjectFromSource(water) == null)
                    water.Profile = profile;

                Reseat(water, profile, tellKws: false);

                // The saved scene starts where m_StartLook says the show starts, so opening
                // MainScene does not put two waters up at once and let one overwrite the other
                // before anyone has pressed play.
                if (name == director.startLook) startWater = water;

                report.Append($"\n  '{name}' -> {Describe(water, profile)}");
            }

            so.ApplyModifiedProperties();

            // EACH ONE REBUILT ALONE. Reseating Settings does not rebuild anything -- the mesh
            // and WorldSpaceBounds are only recomputed when KWS is told the settings moved, and
            // WorldSpaceBounds is a serialised field, so a water left unrebuilt keeps whatever
            // bounds the scene was last saved with. That is how S1's river came to be carrying
            // 500m bounds from back when it was an InfiniteOcean: bounds that size put the
            // camera inside the water volume from anywhere in the room, which turns the
            // underwater rendering on and is nothing like what the section should look like.
            //
            // One at a time, because being told the settings moved is also what makes a water
            // push its shared settings over everyone else's.
            foreach (WaterSystem water in claimed)
            {
                water.enabled = true;
                water.ForceUpdateWaterSettings();
                water.enabled = false;
                EditorUtility.SetDirty(water);
                report.Append($"\n  rebuilt {water.name}: bounds extents {water.WorldSpaceBounds.extents}");
            }

            // AND NOW ONE OF THEM, ALONE. With the list empty this is the only water KWS can
            // push settings onto, so it keeps its own and the ones still off keep theirs.
            if (startWater != null)
            {
                startWater.enabled = true;
                startWater.ForceUpdateWaterSettings();
                EditorUtility.SetDirty(startWater);
                report.Append($"\n  enabled at startup: {startWater.name} ('{director.startLook}')");
            }
            else
            {
                Fail($"no water for the start look '{director.startLook}'; MainScene opens with " +
                     "every water off.");
            }

            foreach (WaterSystem water in waters)
            {
                if (claimed.Contains(water)) continue;

                // Not a failure: MainScene still holds the superseded S1.prefab, deactivated, and
                // a water on an inactive object never registers with KWS. Worth naming, because
                // an unclaimed water that IS active is the one way back into the problem this
                // tool exists to fix.
                bool live = water.gameObject.activeInHierarchy;
                report.Append($"\n  (no look) {water.name} under {Path(water.transform)} " +
                              $"active={live}");
                if (live) Fail($"{water.name} is active but no look owns it; it will fight the " +
                               "section that does.");
            }

            Debug.Log(report.ToString());
            EditorUtility.SetDirty(director);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// The water a look owns: inside the section it already names, or the scene's own when it
        /// names no section.
        ///
        /// Matched through the carrier and root the look was already wired with rather than by
        /// name, so there is one place where a section's identity lives and this agrees with the
        /// lighting by construction.
        /// </summary>
        static WaterSystem FindWaterFor(SerializedProperty look, WaterSystem[] waters,
                                        HashSet<WaterSystem> claimed)
        {
            var carrier = look.FindPropertyRelative("carrier").objectReferenceValue as Component;
            var root = look.FindPropertyRelative("root").objectReferenceValue as GameObject;

            Transform section = carrier != null ? carrier.transform
                              : root != null ? root.transform
                              : null;

            if (section != null)
            {
                foreach (WaterSystem water in waters)
                {
                    if (claimed.Contains(water)) continue;
                    if (water.transform.IsChildOf(section)) return water;
                }
                return null;
            }

            // A look naming no section is the scene's own -- "Onboarding" -- so its water is the
            // one that is not part of any section prefab.
            foreach (WaterSystem water in waters)
            {
                if (claimed.Contains(water)) continue;
                if (PrefabUtility.GetCorrespondingObjectFromSource(water) == null) return water;
            }
            return null;
        }

        /// <summary>
        /// Check MainScene's water against the profiles, without changing anything.
        ///
        /// THE POINT IS THE COMPARISON. Every way this goes wrong -- a reverted prefab override,
        /// an "Apply All", two waters left enabled, a scene saved after KWS had already copied
        /// one water's settings over another's -- ends the same way: a section's water quietly
        /// holding somebody else's numbers. That is invisible in the inspector, because the
        /// values look like perfectly ordinary values. Compared against the profile it is
        /// obvious, so it is compared here rather than noticed in a headset.
        ///
        /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
        ///   -executeMethod CompositeBody.Multiplayer.EditorSetup.SetUpWaterProfiles.Verify
        /// </summary>
        [MenuItem("Tools/Verify/Water")]
        public static void Verify()
        {
            Debug.Log("[VerifyWater] Starting...");
            s_Fail = 0;

            Scene scene = EditorSceneManager.OpenScene(k_Main, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Fail($"could not open {k_Main}.");
                return;
            }

            var director = Object.FindFirstObjectByType<LightingDirector>();
            if (director == null)
            {
                Fail($"{k_Main} has no LightingDirector.");
                return;
            }

            SerializedObject so = new SerializedObject(director);
            SerializedProperty looks = so.FindProperty("m_Looks");
            int enabledCount = 0;

            for (int i = 0; i < looks.arraySize; i++)
            {
                SerializedProperty look = looks.GetArrayElementAtIndex(i);
                string name = look.FindPropertyRelative("name").stringValue;
                var water = look.FindPropertyRelative("water").objectReferenceValue as WaterSystem;

                if (water == null)
                {
                    // Only a problem for a look this project knows has water.
                    if (k_LookProfiles.ContainsKey(name))
                        Fail($"look '{name}' has no water wired; nothing will switch it.");
                    continue;
                }

                if (water.Profile == null)
                {
                    Fail($"look '{name}': {water.name} has no Profile, so nothing re-loads its " +
                         "settings and whatever the scene holds is what it gets.");
                    continue;
                }

                if (water.enabled) enabledCount++;

                CompareWithProfile(name, water);
            }

            // ONE AT A TIME, OR NONE OF THE COMPARISONS ABOVE MEAN ANYTHING. Two registered
            // waters is the state in which KWS overwrites, so finding two here says the next
            // person to save this scene writes the damage to disk.
            if (enabledCount > 1)
                Fail($"{enabledCount} waters are enabled at once. KWS gives the shared settings " +
                     "to whichever registered first, so one of these is not wearing its own.");
            else
                Debug.Log($"[VerifyWater] ok   {enabledCount} water(s) enabled");

            foreach (WaterSystem water in Object.FindObjectsByType<WaterSystem>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!water.gameObject.activeInHierarchy || !water.enabled) continue;
                bool owned = false;
                for (int i = 0; i < looks.arraySize && !owned; i++)
                    owned = looks.GetArrayElementAtIndex(i).FindPropertyRelative("water")
                                 .objectReferenceValue == water;
                if (!owned) Fail($"{water.name} under {Path(water.transform)} is live but no " +
                                 "look owns it.");
            }

            // LAST, because it opens the other scene and everything above is read from this one.
            CheckSectionsAgree();

            // Deliberately not telling anyone to re-run the setup tool: it puts settings back,
            // and a river under a turned section is the one failure here it cannot touch.
            Debug.Log(s_Fail == 0
                ? "[VerifyWater] RESULT: PASS"
                : $"[VerifyWater] RESULT: FAIL - {s_Fail} problem(s). Settings drift is fixed by " +
                  "re-running Tools > Water > Set Up Water Profiles; a turned section is not.");
        }

        /// <summary>
        /// Report where a water's live settings have drifted from its profile, naming the fields
        /// KWS is known to copy between waters separately from the rest -- a mismatch in those is
        /// the fingerprint of an overwrite rather than of someone's unsaved tuning.
        /// </summary>
        static void CompareWithProfile(string look, WaterSystem water)
        {
            WaterSystemScriptableData live = water.Settings;
            WaterSystemScriptableData want = water.Profile;

            if (live == null)
            {
                Fail($"look '{look}': {water.name} has no Settings at all.");
                return;
            }

            var shared = new List<string>();
            if (!Mathf.Approximately(live.GlobalWindSpeed, want.GlobalWindSpeed))
                shared.Add($"GlobalWindSpeed {live.GlobalWindSpeed} != {want.GlobalWindSpeed}");
            if (!Mathf.Approximately(live.GlobalWindTurbulence, want.GlobalWindTurbulence))
                shared.Add($"GlobalWindTurbulence {live.GlobalWindTurbulence} != {want.GlobalWindTurbulence}");
            if (!Mathf.Approximately(live.GlobalWavesAreaScale, want.GlobalWavesAreaScale))
                shared.Add($"GlobalWavesAreaScale {live.GlobalWavesAreaScale} != {want.GlobalWavesAreaScale}");
            if (!Mathf.Approximately(live.GlobalTimeScale, want.GlobalTimeScale))
                shared.Add($"GlobalTimeScale {live.GlobalTimeScale} != {want.GlobalTimeScale}");
            if (live.UseUnderwaterEffect != want.UseUnderwaterEffect)
                shared.Add($"UseUnderwaterEffect {live.UseUnderwaterEffect} != {want.UseUnderwaterEffect}");

            // The ones that decide what the water IS. These are per-instance, so a mismatch here
            // is not an overwrite -- it is the wrong settings object entirely, which is how the
            // river came to be an ocean.
            var own = new List<string>();
            if (live.WaterMeshType != want.WaterMeshType)
                own.Add($"WaterMeshType {live.WaterMeshType} != {want.WaterMeshType}");
            if ((live.SplineScriptableData == null) != (want.SplineScriptableData == null))
                own.Add("spline data " + (live.SplineScriptableData == null ? "missing" : "unexpected"));
            if (live.UseFlowMap != want.UseFlowMap)
                own.Add($"UseFlowMap {live.UseFlowMap} != {want.UseFlowMap}");
            if (live.UseGlobalWind != want.UseGlobalWind)
                own.Add($"UseGlobalWind {live.UseGlobalWind} != {want.UseGlobalWind}");

            // A finite water carrying ocean-sized bounds has not been rebuilt since it stopped
            // being an ocean. WorldSpaceBounds is serialised, so the stale value is on disk, and
            // it is not cosmetic: the bounds are what decides whether the camera counts as being
            // inside the water, so 500m of them turns the underwater rendering on across the
            // whole room.
            if (want.WaterMeshType != WaterSystemScriptableData.WaterMeshTypeEnum.InfiniteOcean
                && water.WorldSpaceBounds.extents.x > 200f)
                own.Add($"bounds extents {water.WorldSpaceBounds.extents} are ocean-sized for a " +
                        $"{want.WaterMeshType}; it has not been rebuilt");

            if (own.Count > 0)
                Fail($"look '{look}': {water.name} is not the water its profile describes - " +
                     string.Join("; ", own));

            if (shared.Count > 0)
                Fail($"look '{look}': {water.name} has been overwritten from another water - " +
                     string.Join("; ", shared) +
                     (water.enabled ? "" : " (and it is not even enabled)"));

            if (own.Count == 0 && shared.Count == 0)
                Debug.Log($"[VerifyWater] ok   '{look}' matches {want.name}");
        }

        static string Describe(WaterSystem water, WaterSystemScriptableData profile)
        {
            return $"{water.name} profile='{profile.name}' " +
                   $"mesh={water.Settings.WaterMeshType} " +
                   $"timeScale={water.Settings.GlobalTimeScale} " +
                   $"underwater={water.Settings.UseUnderwaterEffect}";
        }

        static string Path(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (Transform p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        static void Fail(string message)
        {
            s_Fail++;
            Debug.LogError($"[Water] {message}");
        }
    }
}
