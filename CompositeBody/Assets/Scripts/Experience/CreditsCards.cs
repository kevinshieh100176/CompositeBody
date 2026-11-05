using UnityEngine;

namespace CompositeBody.Experience
{
    /// <summary>Which of the two end cards a <see cref="CreditsCardClip"/> drives.</summary>
    public enum CreditsCard
    {
        /// <summary>The 合成肉身 logotype, the same artwork the piece opens on in O-0.</summary>
        Title,

        /// <summary>The staff list.</summary>
        List,
    }

    /// <summary>
    /// The two cards the piece ends on, held in front of the player's eyes after S3 whites out.
    ///
    /// THE WHITE IS ALREADY THERE. The Fade track's 「S3-6 轉白」 clip runs 1251→1256 and ends at
    /// alpha 1, and <see cref="ScreenFade"/> holds its last value, so the screen stays white with
    /// nothing further driving it. These cards are drawn on top of that white; they do not make
    /// it and they do not clear it.
    ///
    /// WHICH IS WHY THEY ARE NOT ORDINARY CARDS. The fade is a quad parented to the camera eight
    /// centimetres away, at Queue Overlay with ZTest Always -- it beats anything that depth-tests
    /// and anything in an earlier queue. A card placed in the room, O-0's way, would simply be
    /// behind it. So these use CompositeBody/Card, which ignores depth exactly as the fade does
    /// and draws one queue later (see Card.shader).
    ///
    /// AND WHY THEY ARE METRES AWAY RATHER THAN CENTIMETRES. The obvious move -- copy ScreenFade
    /// and parent a quad 8 cm from the eyes -- works for a flat colour and fails for type. At
    /// 8 cm the two eyes see the card from wildly different angles and cannot fuse it; the fade
    /// gets away with it only because a uniform white field has nothing to fuse. The cards hang
    /// at <see cref="m_Distance"/>, far enough to read comfortably, and the depth costs nothing
    /// because ZTest Always means distance never decides what is visible, only what is readable.
    ///
    /// HEAD-LOCKED, deliberately. After a white-out there is no horizon, no room and no reason
    /// for the players to be facing where they were -- a card left in the world would be behind
    /// whoever turned. Both cards ride the camera, so both players see the whole end regardless
    /// of where they are standing or looking.
    ///
    /// BUILT AT RUNTIME, like the fade quad, and not saved. MainScene is a large shared file and
    /// two quads parented under the XR rig would be two more things in every merge; the cards
    /// are created from the materials below and carry <see cref="HideFlags.DontSave"/>.
    /// <see cref="ExecuteAlways"/> so they exist while authoring too -- the whole argument for
    /// the show being one timeline is that it can be scrubbed, and a card that only appears in
    /// play mode cannot be.
    /// </summary>
    [ExecuteAlways]
    public class CreditsCards : MonoBehaviour
    {
        public static CreditsCards Instance { get; private set; }

        [SerializeField, Tooltip("The 合成肉身 logotype. Expects CompositeBody/Card -- an " +
                                 "alpha-blended material, NOT O0Title.mat. That one is additive, " +
                                 "which is right for white type on a black room and invisible " +
                                 "on the white-out: adding anything to white is white.")]
        Material m_TitleMaterial;

        [SerializeField, Tooltip("The staff list. Black type on a transparent background, so it " +
                                 "needs the white behind it and alpha blending to sit on it.")]
        Material m_ListMaterial;

        [SerializeField, Min(0.3f), Tooltip("Metres in front of the eyes. Far enough that the " +
                                            "two eyes can fuse type -- the fade quad's 8 cm is " +
                                            "fine for a flat colour and unreadable for text. " +
                                            "Nothing is occluded by this: the shader ignores " +
                                            "depth, so the distance only buys comfort.")]
        float m_Distance = 2f;

        [SerializeField, Range(4f, 110f),
         Tooltip("How wide the logotype is across the view, in degrees. Larger than the 24° " +
                 "O-0 opens on, deliberately: 24° is a small bright title with a whole black " +
                 "room to itself, and the same 24° on a full white field reads as a sticker in " +
                 "the middle of nowhere. BuildCredits derives this from the artwork so the " +
                 "logotype lands 45° across.")]
        float m_TitleWidthDegrees = 46f;

        [SerializeField, Range(4f, 110f),
         Tooltip("How wide the staff list is across the view, in degrees. The artwork is a 16:9 " +
                 "page with wide margins -- the type itself only covers the middle 31% -- so " +
                 "this has to be generous or the lines come out too small to read. At 70 the " +
                 "type is about 21° wide and each line about 2°.")]
        float m_ListWidthDegrees = 70f;

        [SerializeField, Range(-30f, 30f),
         Tooltip("Degrees below the view axis to hang the cards. 0 is dead centre.")]
        float m_Pitch;

        CardFade m_Title;
        CardFade m_List;

        // The frame each card was last written on. See ClearStale.
        int m_TitleStamp = int.MinValue;
        int m_ListStamp = int.MinValue;

        /// <summary>The logotype, or null before the cards have been built.</summary>
        public CardFade titleCard => m_Title;

        /// <summary>The staff list, or null before the cards have been built.</summary>
        public CardFade listCard => m_List;

        void OnEnable()
        {
            if (Instance != null && Instance != this && Instance.isActiveAndEnabled) return;
            Instance = this;
        }

        /// <summary>
        /// Clears the singleton and nothing else.
        ///
        /// It does NOT destroy the cards, and that is on purpose. OnDisable is what a domain
        /// reload calls on the way out, and DestroyImmediate from inside it is a good way to
        /// get Unity complaining about objects torn down mid-serialisation. The cards do not
        /// need it: they carry <see cref="HideFlags.DontSave"/> so they never reach the scene
        /// file, they sit at alpha 0 with their renderers switched off, and
        /// <see cref="EnsureCard"/> adopts the pair already under the camera rather than making
        /// a second. A reload therefore finds them and carries on.
        /// </summary>
        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Built here rather than on demand so the first frame of the fade is not also the frame
        /// a GameObject is created on. Both cards start at 0, and <see cref="CardFade"/> switches
        /// a renderer off below 0.002, so they cost nothing for the twenty minutes before the end.
        ///
        /// Play mode only. In the editor there is no Start, which is the whole reason
        /// <see cref="SetAlpha"/> builds too.
        /// </summary>
        void Start() => EnsureBuilt();

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;

            // Aim only -- building is Start's job and SetAlpha's. Nothing is created from here,
            // because in edit mode this does not reliably run at all.
            Aim(m_Title, cam);
            Aim(m_List, cam);

            ClearStale();
        }

        /// <summary>
        /// Take down any card nothing is driving.
        ///
        /// WHAT THIS IS FOR: a backwards jump out of the credits. A card's alpha is only written
        /// while its clip is under the playhead, and whatever it was last set to is simply what
        /// it stays at -- so leaving the credits backwards strands the card at full on the
        /// player's face. Forwards that never happens, because both curves end at 0, but
        /// <see cref="ShowTimelineDriver"/> jumps backwards for real: a staff scrub back into S3
        /// to re-run a beat is exactly the case, and it would run the rest of the section with
        /// the staff list pasted over it.
        ///
        /// A frame of slack, because the director evaluates in PreLateUpdate and this runs in
        /// LateUpdate -- normally the same frame, but an evaluation order that puts them a frame
        /// apart would otherwise blink the cards off on every single frame of the credits. Two
        /// frames late on a card that should already be gone is invisible; a strobing credit
        /// roll is not.
        /// </summary>
        void ClearStale()
        {
            if (m_Title != null && m_Title.alpha > 0f && Time.frameCount - m_TitleStamp > 1)
                m_Title.alpha = 0f;

            if (m_List != null && m_List.alpha > 0f && Time.frameCount - m_ListStamp > 1)
                m_List.alpha = 0f;
        }

        /// <summary>
        /// Make both cards if they are not there yet, and put them where they belong.
        ///
        /// CALLED FROM SetAlpha, not just from Start, and that is not belt and braces. Edit mode
        /// does not tick <see cref="LateUpdate"/> on an <see cref="ExecuteAlways"/> component in
        /// any way worth relying on -- it runs when the editor feels like repainting and not
        /// when a tool sets <c>director.time</c> and calls Evaluate. So the first version of this
        /// built the cards in LateUpdate, and scrubbing the show to the credits produced the
        /// empty white room every time. Driving a card is now what brings it into being, which
        /// means the end scrubs like the rest of the piece.
        /// </summary>
        void EnsureBuilt()
        {
            var cam = Camera.main;
            if (cam == null) return;

            EnsureCard(ref m_Title, "CreditsTitleCard", m_TitleMaterial, m_TitleWidthDegrees, cam);
            EnsureCard(ref m_List, "CreditsListCard", m_ListMaterial, m_ListWidthDegrees, cam);

            Aim(m_Title, cam);
            Aim(m_List, cam);
        }

        /// <summary>Put <paramref name="card"/> at <paramref name="value"/>, 0 to 1.</summary>
        public void SetAlpha(CreditsCard card, float value)
        {
            if (m_Title == null || m_List == null) EnsureBuilt();

            if (card == CreditsCard.Title) m_TitleStamp = Time.frameCount;
            else m_ListStamp = Time.frameCount;

            CardFade target = card == CreditsCard.Title ? m_Title : m_List;
            if (target == null) return;

            // Through the property, not the serialized field: the setter is what pushes the
            // value into the renderer's property block. CardFade would catch up on its own
            // LateUpdate, but an editor scrub has no next LateUpdate.
            target.alpha = value;
        }

        void Aim(CardFade card, Camera cam)
        {
            if (card == null) return;

            Transform t = card.transform;

            // Re-parented every frame, not once: CalibrationPoint can move the whole rig out
            // from under the camera mid-session, and the editor can reload the domain between
            // one frame and the next.
            if (t.parent != cam.transform) t.SetParent(cam.transform, false);

            t.localRotation = Quaternion.Euler(m_Pitch, 0f, 0f);
            t.localPosition = t.localRotation * new Vector3(0f, 0f, m_Distance);
        }

        void EnsureCard(ref CardFade card, string name, Material material, float widthDegrees,
                        Camera cam)
        {
            if (card != null)
            {
                Resize(card, material, widthDegrees);
                return;
            }

            if (material == null) return;

            // Adopt one left behind rather than making a second. HideFlags.DontSave keeps these
            // out of the scene file, which also means nothing serializes m_Title across a domain
            // reload: the field comes back null while the GameObject is still parented under the
            // camera, and a blind create would stack a new card on the old one every recompile.
            Transform existing = cam.transform.Find(name);
            if (existing != null)
            {
                card = existing.GetComponent<CardFade>();
                if (card != null)
                {
                    Resize(card, material, widthDegrees);
                    return;
                }
                DestroyImmediate(existing.gameObject);
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.hideFlags = HideFlags.DontSave;
            // Left on Default, which is what CreatePrimitive gives and what the ScreenFade quad
            // runs on. Not the camera's own layer: that reads as cautious and is the opposite --
            // the rig's camera may sit on a layer the mask does not draw.
            WarnIfCulled(cam, go.layer);

            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            go.transform.SetParent(cam.transform, false);

            card = go.AddComponent<CardFade>();
            card.alpha = 0f;
            Resize(card, material, widthDegrees);
        }

        /// <summary>Static, so a domain reload or a second camera does not restart the spam.</summary>
        static bool s_WarnedCulled;

        /// <summary>
        /// Say so if the camera will not draw the layer the cards are on.
        ///
        /// This project has lost a whole section to exactly this once -- S1-1 was invisible in
        /// the headset because its layer never reached the culling mask -- and the failure looks
        /// identical to a material problem from the outside: everything is wired, the alpha
        /// animates, nothing is on screen. One line now is cheaper than that afternoon.
        /// </summary>
        void WarnIfCulled(Camera cam, int layer)
        {
            if (s_WarnedCulled) return;
            if ((cam.cullingMask & (1 << layer)) != 0) return;

            s_WarnedCulled = true;
            Debug.LogError($"[Credits] '{cam.name}' does not draw layer {layer} " +
                           $"('{LayerMask.LayerToName(layer)}'), so the end cards will never " +
                           "appear however the timeline drives them. Add the layer to the " +
                           "camera's culling mask. Said once; it will not repeat.");
        }

        void Resize(CardFade card, Material material, float widthDegrees)
        {
            float width = 2f * m_Distance * Mathf.Tan(widthDegrees * 0.5f * Mathf.Deg2Rad);

            // The artwork's own proportions, so neither card is stretched. 1:1 if the material
            // has lost its texture, which at least leaves something visible to notice.
            var tex = material != null ? material.mainTexture : null;
            float aspect = tex != null && tex.width > 0 ? tex.height / (float)tex.width : 1f;

            // No flip. A Quad's front face looks along its own -Z, and the card sits at +Z in
            // front of a camera that looks along +Z, so identity rotation already has it facing
            // the player with its artwork the right way round -- the same geometry O-0's cards
            // rely on. Mirroring it in the scale would mirror the type with it.
            card.transform.localScale = new Vector3(width, width * aspect, 1f);
        }

        /// <summary>
        /// Throw both cards away, so the next build picks up changed materials or a changed
        /// size. Not called on disable -- see the remarks there -- but useful from the Inspector
        /// while dialling the numbers in.
        /// </summary>
        [ContextMenu("Rebuild the cards")]
        public void Rebuild()
        {
            Dispose(ref m_Title);
            Dispose(ref m_List);
            EnsureBuilt();
        }

        void Dispose(ref CardFade card)
        {
            if (card == null) return;
            GameObject go = card.gameObject;
            card = null;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }
}
