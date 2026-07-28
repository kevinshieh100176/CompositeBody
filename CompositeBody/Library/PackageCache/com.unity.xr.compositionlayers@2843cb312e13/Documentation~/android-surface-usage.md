---
uid: xr-layers-android-surface
---

## Display Android Surface content

You can use XR Composition Layer to efficiently display native, hardware-decoded, or security-sensitive content (such as video) without routing it through Unity’s rendering pipeline. When you are working with Android development, you can render Android Surface content directly to a quad or cylinder composition layers by choosing Android Surface as the source of the [Source Texture](xref:xr-layers-source-textures) component.

### Access Android Surface objects with OpenXR

The [Unity OpenXR Plug-in](xref:openxr-manual) provides composition layers support for Android Surface. To obtain the Android Surface object to use for a layer, you must call the OpenXR [GetLayerAndroidSurfaceObject](xref:UnityEngine.XR.OpenXR.CompositionLayers.OpenXRLayerUtility.GetLayerAndroidSurfaceObject(System.Int32)) in a script.

The following example shows how you can use `GetLayerAndroidSurfaceObject` to access the Android Surface object to use for a layer:

``` csharp
// Get Android Surface Object
IntPtr surface = IntPtr.Zero;
if (CompositionLayerManager.TryGetLayerId(layer, out int layerId))
{
    surface = OpenXRLayerUtility.GetLayerAndroidSurfaceObject(layerId);
}
```
You can access a sample using Android Surface from the Package Manager, as outlined in the following instructions:

1. Open the **Package Manager** (menu: **Window &gt; Package Manager**)
2. Select the **XR Composition Layers** package in the list of packages in your project.
3. Towards the bottom of the window, select the **Samples** tab.
4. Click **Import** next to the **Sample External Android Surface Project** item.

## Additional resources

*  [Android Surface](https://developer.android.com/reference/kotlin/android/view/Surface) (Android developer documentation)
*  [XR_KHR_android_surface_swapchain](https://registry.khronos.org/OpenXR/specs/1.0/html/xrspec.html#XR_KHR_android_surface_swapchain) (Khronos)
