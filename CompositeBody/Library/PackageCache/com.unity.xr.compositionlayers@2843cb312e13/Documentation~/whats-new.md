---
uid: xr-layers-whats-new
---
# What's new in version 2.4

This release includes the following significant changes:

## GetInstanceID is deprecated in Unity 6.4

Across all APIs, whenever a composition layer is identified by an `int Id`, this ID value used to be the layer's Instance ID and now comes from the new API [CompositionLayerManager.TryGetLayerId](xref:Unity.XR.CompositionLayers.Services.CompositionLayerManager.TryGetLayerId*).

> [!IMPORTANT]
> If your app uses `GetInstanceID` to create input parameters for [OpenXRLayerUtility](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.17/api/UnityEngine.XR.OpenXR.CompositionLayers.OpenXRLayerUtility.html), you must migrate to `CompositionLayerManager.TryGetLayerId` for continued support.

## URP compatibility mode is removed in Unity 6.4

- In Unity 6000.4 and newer Editor versions, all methods that depend on URP Compatibility Mode have been changed from `Obsolete(false)` to `Obsolete(true)`. URP Compatibility Mode is removed in Unity 6000.4, so these APIs are no longer supported in Unity 6000.4 or newer. The following methods are affected:
  - `EmulationLayerUniversalScriptableRendererPass.Execute`

## Other changes

- Changed `InteractactableUIMirror` Cameras so they are visible in the inspector.

For a full list of changes in this version including backwards-compatible bugfixes, refer to the package [changelog](xref:xr-layers-changelog).
