using System;
using UnityEngine;

namespace CompositeBody.Experience
{
    /// <summary>
    /// Owns which section's lighting is the one the room is standing in.
    ///
    /// The show walks through rooms that were lit separately and baked separately, and a scene's
    /// lighting is GLOBAL in a way its geometry is not: LightmapSettings.lightmaps is one array
    /// and RenderSettings is one set of fog, ambient and skybox, however many sections happen to
    /// be loaded. <see cref="BakedLightmapCarrier"/> already solved the addressing -- it can
    /// bring a prefab's bake with it and rebase the indices -- but it deliberately stopped
    /// there: a carrier claims the lighting the moment it is enabled, and with two of them on,
    /// one of them loses and says so in the console.
    ///
    /// So something has to decide. This is that something: a named list of looks, exactly one of
    /// them applied, and switching is one call.
    ///
    /// WHY THE GEOMETRY COMES WITH IT. A look can name a root object as well as a carrier, and
    /// the two are switched together. That is not a convenience -- it closes a hole. The global
    /// array is rebuilt from the carriers that are ACTIVE, and a renderer whose carrier has been
    /// switched off keeps the index it was given, now pointing at whatever took that slot.
    /// Turning off S1's carrier while S1 is still in front of the player does not unlight S1, it
    /// relights it with S2's lightmaps, which looks like a corrupt bake rather than a mistake in
    /// the running order. Naming the root means the thing whose lighting just left leaves with
    /// it.
    ///
    /// Leave the root empty where the timeline's own Activation Tracks already show and hide the
    /// section; then this only moves the lighting and the two do not fight.
    ///
    /// A LOOK WITH NEITHER is how you name the scene's own bake. MainScene has 32 lightmaps of
    /// its own and the onboarding room is lit by them, so the look called "Onboarding" holds no
    /// carrier and no root: applying it switches every carrier off, which is precisely what
    /// hands the lighting back to the scene. It needs to be in the list rather than left as an
    /// empty name, because a clip has to be able to ask for it by name on the way back through a
    /// blackout.
    ///
    /// WHY THE WATER COMES WITH IT TOO. KWS has the same shape of problem as the lightmap array,
    /// for the same reason: a section's water is a local thing but a lot of what makes it look
    /// the way it does is not. KWS keeps one FFT wave simulation and one set of underwater
    /// settings for the whole scene, and takes them from whichever water sits at index 0 of
    /// <c>WaterSharedResources.WaterInstances</c>. Worse, <c>AddWaterInstance</c> swaps an
    /// InfiniteOcean straight to index 0, and then copies that instance's wind, waves, time scale
    /// and underwater flags over every other water's settings. So with the onboarding ocean and
    /// S1's river both enabled, the ocean wins: the river gets the ocean's waves whether or not
    /// anyone asked, and because a water's Settings object is serialised into the scene file, the
    /// overwrite is saved and the river's authored look is gone for good. That is how MainScene's
    /// river ended up as a default ocean.
    ///
    /// Switching the water with the look is what keeps them apart: off first, so the list is
    /// empty at the moment the next one registers and there is nothing for it to be overwritten
    /// from. Everything a section genuinely owns -- colour, turbidity, mesh type, the river
    /// spline, flow map, caustics -- is per-instance and survives; only the shared handful has to
    /// take turns, and taking turns is exactly what the running order already does.
    /// </summary>
    [DisallowMultipleComponent]
    public class LightingDirector : MonoBehaviour
    {
        public static LightingDirector Instance { get; private set; }

        /// <summary>One section's lighting, under a name the timeline can ask for.</summary>
        [Serializable]
        public struct Look
        {
            [Tooltip("What the timeline asks for. Any name will do as long as it matches the " +
                     "clip -- 'S1', 'S2'. Compared as typed, so case and spaces count.")]
            public string name;

            [Tooltip("The carrier holding this section's bake, normally on the section's prefab " +
                     "root. Enabled while this look is current, disabled otherwise.")]
            public BakedLightmapCarrier carrier;

            [Tooltip("The section's root, switched on and off with the carrier.\n\n" +
                     "Set this unless something else already shows and hides the section: a " +
                     "renderer left visible after its carrier is switched off gets lit by " +
                     "whichever bake takes its place in the array, which reads as a broken " +
                     "bake. Leave it empty when an Activation Track owns the section.")]
            public GameObject root;

            [Tooltip("The section's KWS water, enabled while this look is current and disabled " +
                     "otherwise. Leave it empty for a section with no water.\n\n" +
                     "Only one water may be enabled at a time: KWS takes the wind, waves, time " +
                     "scale and underwater settings from whichever water registered first and " +
                     "copies them over all the others, so two at once means one of them is not " +
                     "wearing its own look.\n\n" +
                     "Give the water a Settings Profile as well. On the way in its settings are " +
                     "re-loaded from that profile, which is what makes the section's look " +
                     "repeatable rather than whatever the scene file happens to hold -- and what " +
                     "repairs a scene some earlier run has already overwritten. The consequence " +
                     "is that tuning water in the inspector and not pressing 'Save to Profile' " +
                     "shows up in the editor and then vanishes in play mode.")]
            public KWS.WaterSystem water;
        }

        [SerializeField, Tooltip("Every section whose lighting this switches between.")]
        Look[] m_Looks = new Look[0];

        [SerializeField, Tooltip("Applied in Awake, so the show starts in a stated lighting " +
                                 "rather than in whichever carriers happened to be left enabled " +
                                 "in the scene. Empty means touch nothing at startup.")]
        string m_StartLook = "";

        /// <summary>The look applied, or "" for none of them.</summary>
        string m_Current = "";

        public string current => m_Current;

        /// <summary>
        /// The look the show starts in. Read by the setup tooling, which has to leave the saved
        /// scene standing in the same look Awake will ask for -- otherwise opening the scene puts
        /// two sections' water up at once and one overwrites the other before anyone presses
        /// play.
        /// </summary>
        public string startLook => m_StartLook;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Lighting] a second LightingDirector is in the scene; this one " +
                                 "is ignored. The lighting is one set per scene and so is the " +
                                 "thing that owns it.", this);
                return;
            }
            Instance = this;

            // IN AWAKE, NOT START, because by Start the carriers have had their OnEnable and
            // every one that was left enabled in the scene has already claimed the lighting --
            // which is the state this exists to prevent, and which announces itself as a warning
            // about two carriers wanting the environment at once. Switching them off here means
            // the ones that are not wanted never claim it in the first place.
            if (!string.IsNullOrEmpty(m_StartLook)) ApplyLook(m_StartLook);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Switch to a named look, or do nothing if it is already the current one.
        ///
        /// Safe to call every frame, which is how the timeline calls it: the common case is one
        /// string comparison.
        /// </summary>
        public static void Apply(string lookName)
        {
            if (Instance != null) Instance.ApplyLook(lookName);
        }

        public void ApplyLook(string lookName)
        {
            lookName ??= "";
            if (m_Current == lookName) return;

            if (lookName.Length > 0 && IndexOf(lookName) < 0)
            {
                WarnUnknown(lookName);

                // LEAVE THE LIGHTING WHERE IT IS. A typo in a clip should cost a wrong-looking
                // transition, not a black room: applying "nothing" here would strip the bake off
                // the section the players are standing in.
                return;
            }

            // OFF FIRST, THEN ON. Both orders end in the same place, but enabling the new carrier
            // while the old one is still up puts two environments on at once, and the carrier is
            // right to complain about that -- the fog cannot be two colours. Done this way there
            // is never a frame with two, and the one that comes on last, which is the one the
            // carrier hands the environment to, is the one being switched to.
            for (int i = 0; i < m_Looks.Length; i++)
            {
                if (m_Looks[i].name == lookName) continue;
                if (m_Looks[i].carrier != null) m_Looks[i].carrier.enabled = false;
                if (m_Looks[i].root != null) m_Looks[i].root.SetActive(false);

                // Disabling the component is what takes the water out of KWS's instance list --
                // WaterRenderingActive only stops it drawing and would leave it owning the
                // shared wind and waves from off screen.
                if (m_Looks[i].water != null) m_Looks[i].water.enabled = false;
            }

            int index = IndexOf(lookName);
            if (index >= 0)
            {
                // Root first: a carrier on an inactive object cannot take the lighting, so
                // enabling the component before the object would quietly do nothing.
                if (m_Looks[index].root != null) m_Looks[index].root.SetActive(true);
                if (m_Looks[index].carrier != null) m_Looks[index].carrier.enabled = true;
                ApplyWater(m_Looks[index]);
            }

            string from = m_Current.Length > 0 ? m_Current : "(none)";
            m_Current = lookName;

            // Once per switch, not once per frame -- there are a handful of these in a show and
            // they are exactly what you want to see in a log when a section comes up wrong.
            Debug.Log($"[Lighting] {from} -> {(lookName.Length > 0 ? lookName : "(none)")}", this);
        }

        /// <summary>
        /// Hand the shared water state to this look's water, and put that water back on its own
        /// profile on the way in.
        ///
        /// THE RE-LOAD IS THE REPAIR, not a precaution. KWS copies the wind, waves, time scale
        /// and underwater flags of whichever water registered first onto every other water's
        /// Settings, and Settings is a scene object, so once that has happened and the scene has
        /// been saved the section's authored look is gone from disk. Re-cloning the profile here
        /// means the scene file is no longer what decides how a section's water looks -- the
        /// profile asset is -- and a scene already carrying someone else's settings comes back
        /// right without anyone having to notice it was wrong.
        /// </summary>
        static void ApplyWater(in Look look)
        {
            KWS.WaterSystem water = look.water;
            if (water == null) return;

            // BEFORE ENABLING. AddWaterInstance reads WaterMeshType to decide whether this water
            // is the ocean that belongs at index 0, and it reads it off Settings -- so Settings
            // has to be the right one by then, not one frame later.
            if (water.Profile != null)
            {
                var settings = Instantiate(water.Profile);
                settings.name = water.Profile.name;
                water.Settings = settings;
            }

            water.enabled = true;

            // The start look is not switched to, it is simply already on, so nothing above
            // changed its enabled state and KWS has not been told the settings moved underneath
            // it. This is the same call KWS's own 'Load from Profile' button makes.
            water.ForceUpdateWaterSettings();
        }

        int IndexOf(string lookName)
        {
            for (int i = 0; i < m_Looks.Length; i++)
                if (m_Looks[i].name == lookName) return i;
            return -1;
        }

        bool m_WarnedUnknown;

        void WarnUnknown(string lookName)
        {
            // ONCE. This is reached from a timeline clip's ProcessFrame, so a name that does not
            // match is wrong on every frame of the clip, and the show's log is read after the
            // fact by someone looking for the one thing that went wrong.
            if (m_WarnedUnknown) return;
            m_WarnedUnknown = true;

            Debug.LogWarning($"[Lighting] no look called '{lookName}'. The lighting was left as " +
                             $"it was. Known looks: {Known()}. " +
                             "Said once; it will not repeat.", this);
        }

        /// <summary>The look names, for a message that has just rejected one.</summary>
        string Known()
        {
            var known = new System.Text.StringBuilder();
            foreach (Look look in m_Looks)
            {
                if (known.Length > 0) known.Append(", ");
                known.Append('\'').Append(look.name).Append('\'');
            }
            return known.Length > 0 ? known.ToString() : "none";
        }

#if UNITY_EDITOR
        [SerializeField, Tooltip("Which look 'Preview Look In Editor' applies, from this " +
                                 "component's context menu. Authoring only -- it is never read " +
                                 "at runtime.")]
        string m_PreviewLook = "";

        /// <summary>
        /// Stand the scene in one look so it can be authored, in the editor, without pressing
        /// play.
        ///
        /// WHY THIS HAS TO BE ASKED FOR. The timeline clip that drives this at runtime returns
        /// early when the editor is not playing, on purpose: carriers rewrite the global lightmap
        /// array and switch section roots, and a scene being authored should not have that
        /// happening underneath it. But the other half of that choice is that MainScene sits in
        /// whichever look was last saved -- so looking at S1 in MainScene showed the onboarding
        /// ocean lying across it with S1's own water switched off, which reads as the river being
        /// broken rather than as the wrong look being up.
        ///
        /// So: the same switch, run once, when you say so. It leaves the scene dirty and does not
        /// save; saving is how you choose which look MainScene opens in, and that wants to be the
        /// one m_StartLook names.
        /// </summary>
        [ContextMenu("Preview Look In Editor")]
        void PreviewLookInEditor()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[Lighting] not previewing: the show is running and owns the " +
                                 "looks. Stop play mode first.", this);
                return;
            }

            if (string.IsNullOrEmpty(m_PreviewLook))
            {
                Debug.LogWarning($"[Lighting] set Preview Look first. Known looks: {Known()}.", this);
                return;
            }

            // Clearing m_Current rather than trusting it: nothing maintains it in the editor, so
            // a preview of the look it happens to hold would early-out and do nothing.
            m_Current = "";
            ApplyLook(m_PreviewLook);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
            Debug.Log($"[Lighting] previewing '{m_PreviewLook}' in the editor. The scene is now " +
                      $"dirty; save it only if you want MainScene to open in this look, which " +
                      $"should normally be '{m_StartLook}'.", this);
        }

        /// <summary>Say what is wired up, without changing it.</summary>
        [ContextMenu("Report Looks")]
        void ReportLooks()
        {
            if (m_Looks.Length == 0)
            {
                Debug.Log("[Lighting] no looks are wired up.", this);
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append($"[Lighting] {m_Looks.Length} look(s), start '{m_StartLook}':");

            foreach (Look look in m_Looks)
            {
                sb.Append($"\n  '{look.name}'  carrier=")
                  .Append(look.carrier != null ? look.carrier.gameObject.name : "MISSING")
                  .Append("  root=")
                  .Append(look.root != null ? look.root.name : "(not switched)")
                  .Append("  water=")
                  .Append(look.water != null ? look.water.gameObject.name : "(none)");

                // A water with no profile is the one wiring mistake this cannot report at
                // runtime, because it fails by looking slightly wrong rather than by throwing:
                // nothing re-loads its settings, so it keeps whatever the scene file holds and
                // goes on being overwritten by whichever water comes up alongside it.
                if (look.water != null && look.water.Profile == null) sb.Append(" NO PROFILE");
            }

            // Everything KWS shares between waters, so the one that is current can be checked
            // against what the section is supposed to look like.
            sb.Append("\n  shared between all waters: wind, FFT waves, time scale, SSR quality, " +
                      "volumetric/caustic resolution, UseUnderwaterEffect, sorting priority");

            Debug.Log(sb.ToString(), this);
        }
#endif
    }
}
