using UnityEngine;

namespace CompositeBody.Experience
{
    /// <summary>
    /// Fades the player's view to and from a flat colour. The script leans on blackouts to
    /// separate its sections -- O-4 ends on one, S0-1 happens inside one -- so this is a
    /// structural part of the piece rather than a transition effect.
    ///
    /// Implemented as a quad parented to the camera rather than as a post-process, because it
    /// has to work in a headset: a full-screen pass would need to be registered with the
    /// renderer, while a quad a few centimetres in front of each eye fades both of them for
    /// free and survives the camera being reparented by calibration.
    /// </summary>
    public class ScreenFade : MonoBehaviour
    {
        public static ScreenFade Instance { get; private set; }

        [SerializeField, Tooltip("Expects the CompositeBody/ScreenFade shader. Created at runtime if left empty.")]
        Material m_Material;

        [SerializeField, Tooltip("Starts opaque, so the first beat can fade up from nothing.")]
        float m_StartAlpha = 1f;

        [SerializeField, Min(0.01f), Tooltip("Distance in front of the camera. Must sit inside the near clip plane.")]
        float m_Distance = 0.08f;

        static readonly int k_AlphaId = Shader.PropertyToID("_Alpha");
        static readonly int k_ColorId = Shader.PropertyToID("_FadeColor");

        Transform m_Quad;
        MeshRenderer m_Renderer;

        float m_Current;
        float m_Target;
        float m_Speed;

        public float alpha => m_Current;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            m_Current = Mathf.Clamp01(m_StartAlpha);
            m_Target = m_Current;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void LateUpdate()
        {
            if (m_Speed > 0f && !Mathf.Approximately(m_Current, m_Target))
            {
                m_Current = Mathf.MoveTowards(m_Current, m_Target, m_Speed * Time.deltaTime);
            }

            EnsureQuad();
            if (m_Quad == null) return;

            // Re-aimed every frame: the XR camera moves with the head, and CalibrationPoint can
            // shift the whole rig underneath it mid-session.
            var cam = Camera.main;
            if (cam != null)
            {
                m_Quad.SetPositionAndRotation(
                    cam.transform.position + cam.transform.forward * m_Distance,
                    cam.transform.rotation);
            }

            if (m_Material != null) m_Material.SetFloat(k_AlphaId, m_Current);

            // A fully transparent quad still costs a draw call and can still sort badly over
            // anything else in the overlay queue, so it is switched off outright.
            if (m_Renderer != null) m_Renderer.enabled = m_Current > 0.001f;
        }

        void EnsureQuad()
        {
            if (m_Quad != null) return;

            var cam = Camera.main;
            if (cam == null) return;

            if (m_Material == null)
            {
                var shader = Shader.Find("CompositeBody/ScreenFade");
                if (shader == null)
                {
                    Debug.LogError("[ScreenFade] Shader 'CompositeBody/ScreenFade' not found; no fade will be drawn.");
                    return;
                }
                m_Material = new Material(shader) { name = "ScreenFade (runtime)" };
                m_Material.SetColor(k_ColorId, Color.black);
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "ScreenFadeQuad";
            Destroy(go.GetComponent<Collider>());

            m_Renderer = go.GetComponent<MeshRenderer>();
            m_Renderer.sharedMaterial = m_Material;
            m_Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Renderer.receiveShadows = false;

            m_Quad = go.transform;
            m_Quad.SetParent(cam.transform, false);

            // Wide enough to cover the full field of view at this distance with margin to spare.
            m_Quad.localScale = new Vector3(m_Distance * 6f, m_Distance * 6f, 1f);
        }

        /// <summary>Fade towards opaque. <paramref name="seconds"/> of 0 snaps.</summary>
        public void FadeOut(float seconds) => FadeTo(1f, seconds);

        /// <summary>Fade towards clear. <paramref name="seconds"/> of 0 snaps.</summary>
        public void FadeIn(float seconds) => FadeTo(0f, seconds);

        public void FadeTo(float targetAlpha, float seconds)
        {
            m_Target = Mathf.Clamp01(targetAlpha);

            if (seconds <= 0f)
            {
                m_Current = m_Target;
                m_Speed = 0f;
                return;
            }

            m_Speed = Mathf.Abs(m_Target - m_Current) / seconds;
            if (m_Speed <= 0f) m_Speed = 1f;
        }

        public void SetFadeColor(Color color)
        {
            if (m_Material != null) m_Material.SetColor(k_ColorId, color);
        }
    }
}
