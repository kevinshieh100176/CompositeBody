using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Unity.XR.CompositionLayers.Layers;
using Unity.XR.CompositionLayers.Services;
using Unity.XR.CompositionLayers.Extensions;

#if UNITY_XR_INTERACTION_TOOLKIT
using System;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Unity.XR.CoreUtils;

#if UNITY_XR_INTERACTION_TOOLKIT_3_0
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
#endif
#endif

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

namespace Unity.XR.CompositionLayers.UIInteraction
{
    /// <summary>
    /// Handles Canvas and Composition Layer Syncing (Size, Scale, etc)
    /// and transforming raycasts from the Composition Layer to the attached Canvas
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(CompositionLayer), typeof(TexturesExtension))]
    public class InteractableUIMirror : MonoBehaviour
    {
        // Minimum size for the canvas to prevent it from being too small
        const float MINIMUM_CANVAS_SIZE = 0.001f;

#if UNITY_XR_INTERACTION_TOOLKIT
        // reference to the Interactable for XRI interaction
        [SerializeField, HideInInspector]
        XRSimpleInteractable xrSimpleInteractable;

        // Reference to the camera that will be used to display the UI
        [SerializeField, HideInInspector]
        Camera canvasCamera;

        // Reference to the render texture the UI camera will render to
        [SerializeField, HideInInspector]
        RenderTexture canvasCameraRenderTexture;

        // Reference to the Tracked DeviceGraphic Raycaster for XRI interaction
        [SerializeField, HideInInspector]
        TrackedDeviceGraphicRaycaster trackedDeviceGraphicRaycaster;

        // Reference to the mesh collider for raycasting
        [SerializeField, HideInInspector]
        MeshCollider meshCollider;

        // Reference to this object's UIHandle and UIFocus
        [SerializeField, HideInInspector]
        RectTransformData compositionLayerRectTransformData;

        // Reference to either a QuadUIScale or CylinderUIScale to handle colliders
        [SerializeField, HideInInspector]
        LayerUIScale layerUIScale;

        // Reference to a canvas group which allows for raycast control
        [SerializeField, HideInInspector]
        CanvasGroup canvasGroup;

        // Reference to the texture extension that the camera render texture will hook into
        [SerializeField, HideInInspector]
        TexturesExtension texturesExtension;

        // Reference to the composition layer the UI will be mirroring to
        [SerializeField, HideInInspector]
        CompositionLayer compositionLayer;

        // Reference to the RectTransform of the canvas
        [SerializeField, HideInInspector]
        RectTransform canvasRectTransform;

        List<IXRRayProvider> interactors = new List<IXRRayProvider>();
        Canvas canvas;
        ProxyInteractorFactory proxyInteractorFactory;
        CanvasHitCalculator canvasHitCalculator;

        CanvasAndCameraSynchronizer canvasAndCameraSynchronizer;
        CameraTargetTextureFactory cameraTargetTextureFactory;
        CanvasLayerCameraController canvasLayerCameraController;

        /// <summary>
        /// Data for each RectTransform in the hierarchy, used to add components if they don't exist
        /// </summary>
        [Serializable]
        sealed class RectTransformData
        {
            public UIHandle m_UIHandle;
            public UIFocus m_UIFocus;
            public Canvas m_Canvas;
        }

        // Dictionary of RectTransforms and their data
        Dictionary<RectTransform, RectTransformData> rectTransformData = new Dictionary<RectTransform, RectTransformData>();

        void Awake()
        {
            InitializeCanvas();
        }

        void Start()
        {
            compositionLayer = GetComponent<CompositionLayer>();
            proxyInteractorFactory = new ProxyInteractorFactory();
            canvasHitCalculator = new CanvasHitCalculator(canvas, gameObject);

#if UNITY_EDITOR
            // Hide textures extension component
            texturesExtension = GetComponent<TexturesExtension>();
            texturesExtension.hideFlags = HideFlags.HideInInspector;

            EditorApplication.quitting += OnEditorApplicationQuitting;
#endif
            // Force canvas into WorldSpace render mode and set it's gameObject layer
            canvas.renderMode = RenderMode.WorldSpace;
            CanvasLayerController.CreateAndSetCanvasLayer(canvas);
            canvasLayerCameraController = new CanvasLayerCameraController(canvas.gameObject);

            // Only add components and init synchronizer if the Editor is not playing (like when creating or duplicating an object within the Inspector)
            if (!Application.isPlaying)
            {
                AddComponents();
            }
            else
            {
                // Subscribe to hover events for when application is actually running.
                xrSimpleInteractable.hoverEntered.AddListener(OnHoverEnter);
                xrSimpleInteractable.hoverExited.AddListener(OnHoverExit);
            }

            EnsureCameraWithRenderTexture();

            // Synchronize the canvas and camera.
            canvasAndCameraSynchronizer = new CanvasAndCameraSynchronizer(canvas, canvasCamera);
            SyncTexturesExtensionWithCameraTarget();
        }

        void CreateAndSetCanvas()
        {
            var canvasGameObject = new GameObject("Canvas");
# if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(canvasGameObject, "Create Canvas GameObject");
            Undo.SetTransformParent(canvasGameObject.transform, transform, "Set canvas parent");
#else
            canvasGameObject.transform.SetParent(transform);
#endif
            canvasGameObject.layer = LayerMask.NameToLayer("UI");
            canvas = GetOrAddComponent<Canvas>(canvasGameObject);
        }

        void InitializeCanvas()
        {
            canvas = GetComponentInChildren<Canvas>();

            if (canvas == null)
                CreateAndSetCanvas();

            canvasRectTransform = canvas.GetComponent<RectTransform>();

            GetOrAddComponent<UIHandle>(canvas.gameObject);
            GetOrAddComponent<UIFocus>(canvas.gameObject);

            canvas.renderMode = RenderMode.WorldSpace;
            GetOrAddComponent<CanvasScaler>(canvas.gameObject);
            GetOrAddComponent<GraphicRaycaster>(canvas.gameObject);
#if UNITY_EDITOR
            // Create default panel if no children exist
            if (canvas.transform.childCount == 0)
            {
                GameObject panelGameObject = DefaultControls.CreatePanel(new DefaultControls.Resources { background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd") });
                panelGameObject.transform.SetParent(canvas.transform, false);
                panelGameObject.GetComponent<UnityEngine.UI.Image>().color = Color.white;
                panelGameObject.layer = canvas.gameObject.layer;
                panelGameObject.transform.SetParent(canvas.transform, false);
                Undo.RegisterCreatedObjectUndo(panelGameObject, "Created ui panel");
            }
#endif
        }

        /// <summary>
        /// Updates the texture extension with the new render textures
        /// if there was a change in the canvas rect or scale
        /// </summary>
        void SyncTexturesExtensionWithCameraTarget()
        {
#if UNITY_EDITOR
            if (canvasAndCameraSynchronizer != null)
            {
                if (canvasAndCameraSynchronizer.Sync())
                {
                    texturesExtension.LeftTexture = canvasCamera.targetTexture;
                    texturesExtension.RightTexture = canvasCamera.targetTexture;
                }
            }
            else if (canvas != null && canvasCamera != null)
            {
                canvasAndCameraSynchronizer = new CanvasAndCameraSynchronizer(canvas, canvasCamera);
            }
#endif
        }

        void Update()
        {
#if UNITY_EDITOR
            // Keep everything in sync if developer edits the canvas or comp layer sizes.
            SyncTexturesExtensionWithCameraTarget();
            SyncLayerUIScaleWithLayerType();
            HandleCanvasSize();

            if (compositionLayer != null && canvasCamera != null)
            {
                var currentPrefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                if (currentPrefabStage != null && currentPrefabStage == PrefabStageUtility.GetPrefabStage(compositionLayer.gameObject))
                {
                    if (canvasCamera.scene != currentPrefabStage.scene)
                        canvasCamera.scene = currentPrefabStage.scene;
                    canvasCamera.Render();
                }
            }
#endif

            // Calculate canvas hits with interactors that have been registered inside OnHoverEnter
            foreach (var interactor in interactors)
            {
                var proxyInteractor = proxyInteractorFactory.GetProxy(interactor);
                if (proxyInteractor == null)
                    continue;

                if (canvasHitCalculator.CalculateCanvasHit(interactor, out Pose hitPose))
                {
                    proxyInteractor.transform.position = hitPose.position;
                    proxyInteractor.transform.rotation = hitPose.rotation;
                }
            }

        }

        /// <summary>
        /// Creates a camera to be used for rendering the UI canvas
        /// The created camera is assigned to canvasCamera
        /// </summary>
        /// <seealso cref="canvasCamera"/>
        void CreateCamera()
        {
            // Check in case gameObject has been duplicated.
            if (canvasCamera == null)
            {
                var newCanvasCameraGameObject = new GameObject("CanvasCamera");
#if UNITY_EDITOR
                Undo.RegisterCreatedObjectUndo(newCanvasCameraGameObject, "Create canvas camera");
#endif
                canvasCamera = GetOrAddComponent<Camera>(newCanvasCameraGameObject);
            }

            var cameraDistance = -100;
            var cameraGameObject = canvasCamera.gameObject;
            cameraGameObject.transform.parent = canvas.transform;
            cameraGameObject.transform.localScale = Vector3.one;
            cameraGameObject.transform.localPosition = new Vector3(0, 0, cameraDistance);
            cameraGameObject.transform.localRotation = Quaternion.Euler(0, 0, 0);
            canvasCamera.nearClipPlane = 0f;
            canvasCamera.clearFlags = CameraClearFlags.SolidColor;
            canvasCamera.backgroundColor = new Color(0, 0, 0, 0.001f);

#if !UNITY_RENDER_PIPELINES_UNIVERSAL
            canvasCamera.stereoTargetEye = StereoTargetEyeMask.None;
#endif
            canvasCamera.orthographic = true;
            canvasCamera.gameObject.layer = canvas.gameObject.layer;
        }

        /// <summary>
        /// General initialization such as creating a camera for the ui, adding colliders and interactables to the layer,
        /// and updating the textures off the composition layer to the UI camera
        /// </summary>
        void AddComponents()
        {
            xrSimpleInteractable = GetOrAddComponent<XRSimpleInteractable>(gameObject);
            meshCollider = GetOrAddComponent<MeshCollider>(gameObject);
            trackedDeviceGraphicRaycaster = GetOrAddComponent<TrackedDeviceGraphicRaycaster>(canvas.gameObject);
            compositionLayerRectTransformData = new RectTransformData
            {
                m_UIHandle = GetOrAddComponent<UIHandle>(gameObject),
                m_UIFocus = GetOrAddComponent<UIFocus>(gameObject)
            };

            SyncLayerUIScaleWithLayerType();

            // Create canvas group
            if (!canvas.TryGetComponent<CanvasGroup>(out canvasGroup))
                canvasGroup = GetOrAddComponent<CanvasGroup>(canvas.gameObject);


            canvasGroup.blocksRaycasts = false;
        }

        /// <summary>
        /// Adds and removes LayerUIScale components based on their type
        /// </summary>
        /// <remarks>(i.e. a changing from a Cylinder to a Quad layer will remove CylinderUIScale and add QuadUIScale)</remarks>
        void SyncLayerUIScaleWithLayerType()
        {
#if UNITY_EDITOR
            if (compositionLayer == null)
                return;

            var layerType = compositionLayer.LayerData.GetType();
            bool isChangedFromQuad = layerType == typeof(QuadLayerData) && layerUIScale is not QuadUIScale;
            bool isChangedFromCylinder = layerType == typeof(CylinderLayerData) && layerUIScale is not CylinderUIScale;

            if (!isChangedFromQuad && !isChangedFromCylinder)
                return;

            if (compositionLayer.LayerData.GetType() == typeof(QuadLayerData))
            {
                if (layerUIScale != null && layerUIScale is not QuadUIScale)
                {
                    Undo.DestroyObjectImmediate(layerUIScale);
                    layerUIScale = (LayerUIScale)Undo.AddComponent(this.gameObject, typeof(QuadUIScale));
                }
                else
                    layerUIScale = (LayerUIScale)Undo.AddComponent(this.gameObject, typeof(QuadUIScale));
            }

            else if (compositionLayer.LayerData.GetType() == typeof(CylinderLayerData))
            {
                if (layerUIScale != null && layerUIScale is not CylinderUIScale)
                {
                    Undo.DestroyObjectImmediate(layerUIScale);
                    layerUIScale = (LayerUIScale)Undo.AddComponent(this.gameObject, typeof(CylinderUIScale));
                }
                else
                    layerUIScale = (LayerUIScale)Undo.AddComponent(this.gameObject, typeof(CylinderUIScale));
            }
#endif
        }

        /// <summary>
        /// Ensures the canvas is at least MINIMUM_CANVAS_SIZE on both width and height
        /// </summary>
        void HandleCanvasSize()
        {
            var width = Mathf.Max(canvasRectTransform.rect.width, MINIMUM_CANVAS_SIZE);
            var height = Mathf.Max(canvasRectTransform.rect.height, MINIMUM_CANVAS_SIZE);

            if(width != canvasRectTransform.rect.width || height != canvasRectTransform.rect.height)
                canvasRectTransform.sizeDelta = new Vector2(width, height);
        }

        /// <summary>
        /// Cleans up creations from AddComponents such as the canvas camera, as well as coliders and render textures
        /// </summary>
        /// <seealso cref="AddComponents"/>
        void DestroyComponents()
        {
            // Only do this for when canvas is no longer a child or component is removed.
            if (gameObject.activeInHierarchy)
            {
                UtilDestroy(layerUIScale);
                UtilDestroy(xrSimpleInteractable);
                UtilDestroy(meshCollider);
                if (compositionLayerRectTransformData != null)
                {
                    UtilDestroy(compositionLayerRectTransformData.m_UIHandle);
                    UtilDestroy(compositionLayerRectTransformData.m_UIFocus);
                }
            }

            // Remove components from UI elements
            if (canvas.gameObject.activeInHierarchy)
            {
                foreach (var rectTransformData in rectTransformData)
                {
                    UtilDestroy(rectTransformData.Value?.m_UIHandle);
                    UtilDestroy(rectTransformData.Value?.m_UIFocus);
                }
            }

            cameraTargetTextureFactory?.ReleaseTargetTexture(canvasCamera);
            texturesExtension.LeftTexture = null;
            texturesExtension.RightTexture = null;
            UtilDestroy(canvasCamera.gameObject);
            UtilDestroy(trackedDeviceGraphicRaycaster);
        }

        /// <summary>
        /// Looks for a component and returns it if found, else will add and return it.
        /// </summary>
        /// <typeparam name="T">The Component to get or add</typeparam>
        /// <returns>the found component or the added component if not found</returns>
        T GetOrAddComponent<T>(GameObject gameObj) where T : Component
        {
            var component = gameObj.GetComponent<T>();
            if (component == null)
#if UNITY_EDITOR
                component = Undo.AddComponent<T>(gameObj);
#else
                component = gameObj.AddComponent<T>();
#endif
            return component;
        }

        void UtilDestroy(UnityEngine.Object obj)
        {
            if (obj == null) return;

            UnityObjectUtils.Destroy(obj);
        }

        void OnHoverEnter(HoverEnterEventArgs args)
        {
            if (args.interactorObject is not IXRRayProvider interactor)
                return;

            if (!proxyInteractorFactory.TryCreateOrFind(interactor, canvas.transform.position, out var proxyInteractor))
                return;

            // Set the real interactor and proxy interactor raycast masks.
            int layerBit = canvas.gameObject.layer;

            switch(interactor)
            {
#if UNITY_XR_INTERACTION_TOOLKIT_3_0
                case NearFarInteractor:
                    break;
#endif
                case XRRayInteractor:
                    // Remove the layer from the real interactor mask
                    (interactor as XRRayInteractor).raycastMask &= ~(1 << layerBit);

                    // Set the proxy interactor mask layer to this layer
                    (proxyInteractor as XRRayInteractor).raycastMask = (1 << layerBit);
                    break;
            }


            // Alow the canvas to receive raycasts
            if (canvasGroup)
                canvasGroup.blocksRaycasts = true;

            interactors.Add(interactor);
        }

        void OnHoverExit(HoverExitEventArgs args)
        {
            if (args.interactorObject is not IXRRayProvider interactor)
                return;

            interactors.Remove(interactor);
        }

        void OnDestroy()
        {
            CanvasLayerController.SetCanvasLayerToDefault(canvas);

            if (canvasLayerCameraController != null)
                canvasLayerCameraController.Dispose();
#if UNITY_EDITOR
            EditorApplication.quitting -= OnEditorApplicationQuitting;

            if (xrSimpleInteractable != null)
            {
                xrSimpleInteractable.hoverEntered.RemoveListener(OnHoverEnter);
                xrSimpleInteractable.hoverExited.RemoveListener(OnHoverExit);
            }

            if (texturesExtension != null)
                texturesExtension.hideFlags = HideFlags.None;
            DestroyComponents();
#else
            if (canvas != null)
                CompositionLayerUtils.UserLayers.UnOccupyBlankLayer(canvas.gameObject);
#endif
        }

#if UNITY_EDITOR
        void OnEditorApplicationQuitting()
        {
            // Set layer to default and clean up layers before saving the project during Editor shutdown
            CanvasLayerController.SetCanvasLayerToDefault(canvas);
        }
#endif

        void EnsureCameraWithRenderTexture()
        {
            if (cameraTargetTextureFactory == null)
                cameraTargetTextureFactory = new CameraTargetTextureFactory();

            if (canvasCamera == null)
                CreateCamera();

            if (canvasCamera.targetTexture == null || canvasCamera.activeTexture == null)
            {
                var targetTexture = cameraTargetTextureFactory.CreateTargetTexture(canvasCamera, canvas.GetComponent<RectTransform>().rect);
                texturesExtension.LeftTexture = targetTexture;
                texturesExtension.RightTexture = targetTexture;
            }

            canvasCamera.cullingMask = 1 << canvas.gameObject.layer;
        }


#if UNITY_EDITOR
        void OnEnable()
        {
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        }

        void OnHierarchyChanged()
        {
            RectTransform[] rectTransforms = canvas.GetComponentsInChildren<RectTransform>(true);

            foreach (RectTransform rectTransform in rectTransforms)
            {
                if (!rectTransformData.ContainsKey(rectTransform))
                {
                    rectTransformData[rectTransform] = new RectTransformData
                    {
                        m_UIHandle = rectTransform.GetComponent<UIHandle>(),
                        m_UIFocus = rectTransform.GetComponent<UIFocus>(),
                        m_Canvas = rectTransform.GetComponent<Canvas>()
                    };
                }

                var data = rectTransformData[rectTransform];
                if (data.m_UIHandle == null && data.m_Canvas == null)
                    data.m_UIHandle = rectTransform.gameObject.AddComponent<UIHandle>();

                if (data.m_UIFocus == null && data.m_Canvas == null)
                    data.m_UIFocus = rectTransform.gameObject.AddComponent<UIFocus>();
            }
        }

#endif //UNITY_EDITOR
#endif //UNITY_XR_INTERACTION_TOOLKIT
    }
}
