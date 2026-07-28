using Unity.XR.CompositionLayers.Services;
using UnityEditor;
using UnityEngine;

namespace Unity.XR.CompositionLayers.UIInteraction
{
    /// <summary>
    /// Helper for creating and deleting Composition Layer Canvas layers
    /// </summary>
    static class CanvasLayerController
    {
        // Prefix to add before the created layer
        internal const string CanvasLayerTagPrefix = "Canvas_";

        /// <summary>
        /// Creates a layer for supplied Canvas
        /// </summary>
        /// <param name="canvas">Canvas to create layer for</param>
        public static void CreateAndSetCanvasLayer(Canvas canvas)
        {
#if UNITY_EDITOR
            // Ensure a unique name for a blank layer
#if UNITY_6000_4_OR_NEWER
            var canvasLayerId = canvas.GetEntityId();
#else
            var canvasLayerId = canvas.GetInstanceID();
#endif
            string canvasLayerTag = CanvasLayerTagPrefix + canvasLayerId.ToString();
            if (!TagManagerController.TryAddLayer(canvasLayerTag))
            {
                Debug.LogError("Unable to add new canvas layer, try removing some unused layers in Project Settings to make space.");
                return;
            }
            int canvasLayerBit = LayerMask.NameToLayer(canvasLayerTag);
            ChangeLayerOfAllChildren(canvas.gameObject, canvasLayerBit);

            // Remove canvas layer from all cameras
            Tools.visibleLayers &= ~(1 << canvasLayerBit);
#else
            // Naming layers does nothing in runtime build, so we use the utils instead.
            int canvasLayerBit = CompositionLayerUtils.UserLayers.OccupyBlankLayer(canvas.gameObject);
#endif
        }

        /// <summary>
        /// Sets Canvas back to default layer before CreateAndSetCanvasLayer
        /// </summary>
        /// <param name="canvas">Canvas to set layer back to default</param>
        /// <seealso cref="CreateAndSetCanvasLayer"/>
        public static void SetCanvasLayerToDefault(Canvas canvas)
        {
#if UNITY_EDITOR
            TagManagerController.RemoveLayer(LayerMask.LayerToName(canvas.gameObject.layer));
#endif
            // Remove layer and set canvas to default layer
            if (canvas != null)
            {
                CompositionLayerUtils.UserLayers.UnOccupyBlankLayer(canvas.gameObject);
            }
        }

        /// <summary>
        /// Changes layer of all children to specified layer
        /// </summary>
        /// <param name="gameObj">GameObject to change all children of</param>
        /// <param name="layerBit">Layer to change children to</param>
        static void ChangeLayerOfAllChildren(GameObject gameObj, int layerBit)
        {
            gameObj.layer = layerBit;
            foreach (Transform t in gameObj.transform)
                ChangeLayerOfAllChildren(t.gameObject, layerBit);
        }
    }
}
