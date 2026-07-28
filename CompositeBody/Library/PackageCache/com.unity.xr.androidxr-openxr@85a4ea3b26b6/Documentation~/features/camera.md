---
uid: androidxr-openxr-camera
---
# Camera

On Android XR devices, AR Foundation's Camera subsystem controls [passthrough](#passthrough).

This page supplements the AR Foundation [Camera](xref:arfoundation-camera) manual. The following sections only contain information about APIs where Google's Android XR runtime exhibits platform-specific behavior.

[!include[](../snippets/arf-docs-tip.md)]

## Optional feature support

Android XR implements the following optional features of AR Foundation's [XRCameraSubsystem](xref:UnityEngine.XR.ARSubsystems.XRCameraSubsystem):

| Feature | Descriptor Property | Supported |
| :------ | :--------------- | :--------: |
| **Brightness** | [supportsAverageBrightness](xref:UnityEngine.XR.ARSubsystems.XRCameraSubsystemDescriptor.supportsAverageBrightness) | Yes |
| **Color temperature** | [supportsAverageColorTemperature](xref:UnityEngine.XR.ARSubsystems.XRCameraSubsystemDescriptor.supportsAverageColorTemperature) | Yes |
| **Color correction** | [supportsColorCorrection](xref:UnityEngine.XR.ARSubsystems.XRCameraSubsystemDescriptor.supportsColorCorrection) | Yes |
| **Display matrix** | [supportsDisplayMatrix](xref:UnityEngine.XR.ARSubsystems.XRCameraSubsystemDescriptor.supportsDisplayMatrix) | |
| **Projection matrix** | [supportsProjectionMatrix](xref:UnityEngine.XR.ARSubsystems.XRCameraSubsystemDescriptor.supportsProjectionMatrix) | |
| **Timestamp** | [supportsTimestamp](xref:UnityEngine.XR.ARSubsystems.XRCameraSubsystemDescriptor.supportsTimestamp) | |
| **Camera configuration** | [supportsCameraConfigurations](xref:UnityEngine.XR.ARSubsystems.XRCameraSubsystemDescriptor.supportsCameraConfigurations) | |
| **Camera image** | [supportsCameraImage](xref:UnityEngine.XR.ARSubsystems.XRCameraSubsystemDescriptor.supportsCameraImage) | |
| **Average intensity in lumens** | [supportsAverageIntensityInLumens](xref:UnityEngine.XR.ARSubsystems.XRCameraSubsystemDescriptor.supportsAverageIntensityInLumens) | Yes |
| **Focus modes** | [supportsFocusModes](xref:UnityEngine.XR.ARSubsystems.XRCameraSubsystemDescriptor.supportsFocusModes) | |
| **Face tracking ambient intensity light estimation** | [supportsFaceTrackingAmbientIntensityLightEstimation](xref:UnityEngine.XR.ARSubsystems.XRCameraSubsystemDescriptor.supportsFaceTrackingAmbientIntensityLightEstimation) | |
| **Face tracking HDR light estimation** | [supportsFaceTrackingHDRLightEstimation](xref:UnityEngine.XR.ARSubsystems.XRCameraSubsystemDescriptor.supportsFaceTrackingHDRLightEstimation) | |
| **World tracking ambient intensity light estimation** | [supportsWorldTrackingAmbientIntensityLightEstimation](xref:UnityEngine.XR.ARSubsystems.XRCameraSubsystemDescriptor.supportsWorldTrackingAmbientIntensityLightEstimation) | Yes |
| **World tracking HDR light estimation** | [supportsWorldTrackingHDRLightEstimation](xref:UnityEngine.XR.ARSubsystems.XRCameraSubsystemDescriptor.supportsWorldTrackingHDRLightEstimation) | Yes |
| **Camera grain** | [supportsCameraGrain](xref:UnityEngine.XR.ARSubsystems.XRCameraSubsystemDescriptor.supportsCameraGrain) | |
| **Image stabilization** | [supportsImageStabilization](xref:UnityEngine.XR.ARSubsystems.XRCameraSubsystemDescriptor.supportsImageStabilization) | |
| **Exif data** | [supportsExifData](xref:UnityEngine.XR.ARSubsystems.XRCameraSubsystemDescriptor.supportsExifData) | |

> [!NOTE]
> Refer to AR Foundation [Camera platform support](xref:arfoundation-camera-platform-support) for more information
> on the optional features of the camera subsystem.

## Passthrough

The passthrough camera captures real-time images of the external environment to provide the user with a view of their surroundings while wearing a headset. You can use passthrough to implement immersive mixed-reality experiences, by layering virtual content on top of passthrough images of the surrounding environment.

### Enable passthrough

Enable the [AR Camera Manager component](xref:arfoundation-camera-components#ar-camera-manager-component) to enable passthrough, and disable it to disable passthrough.

### AR Camera Background component

Android XR passthrough doesn't require the [AR Camera Background component](xref:arfoundation-camera-components#ar-camera-background-component). If `ARCameraBackground` is in your scene, it has no effect on Android XR devices. If your scene only targets Android XR devices, you can safely delete the AR Camera Background component from your XR Origin's **Main Camera** GameObject.

### Configure camera background

Android XR passthrough requires that your Camera has a transparent background. To do this, set your **Background Color** (Universal Render Pipeline) or **Clear Flags** (Built-In Render Pipeline) to **Solid Color**, with the **Background** alpha channel value set to `0`.

Refer to [Configure camera background for passthrough](xref:androidxr-openxr-scene-setup#camera-background-passthrough) to understand how to set your camera background.

> [!IMPORTANT]
> In Unity 6.5 and newer, the Built-In Render Pipeline is deprecated and will be made obsolete in a future release. For more information, refer to [Migrating from the Built-In Render Pipeline to URP](https://docs.unity3d.com/6000.5/Documentation/Manual/urp/upgrading-from-birp.html) and [Render pipeline feature comparison](https://docs.unity3d.com/6000.5/Documentation/Manual/render-pipelines-feature-comparison.html).

### Passthrough dimming control

You can control global passthrough dimming on supported Android XR devices.

#### Overview of dimming

Dimming behavior can vary by device type, runtime support, passthrough state, and environment blend mode. Your app can read and request dimming levels through the scripting API when the Android XR: AR Camera feature is enabled and the device runtime supports the `XR_ANDROID_global_passthrough_dimming` extension.

The following sections explain how Android XR handles switching between blend modes, device-specific behavior, blend mode considerations, and how to use the dimming APIs.

Dimming values are perceptual and don't correspond to a linear physical change on every device. Remember and reapply the preferred dimming level when needed, when using dimming in your app.

> [!WARNING]
> Be aware that your system can reduce or override dimming for safety-related reasons, such as when you approach a boundary.

#### Dimming and blend mode behavior

On supported runtimes, app-requested dimming overrides the default blend-mode-based dimming behavior. With the Android XR: AR Camera feature, the Android XR package switches to **Alpha** or **Opaque** depending on the enabled/disabled status of the **AR Camera Manager component**, and consequently the passthrough state.

The default dimming behavior can vary by device runtime and blend mode until your app explicitly requests a dimming value. After your app requests a dimming value, the runtime uses that requested value instead of the default blend-mode-based dimming behavior.

Default runtime behavior for dimming is 100% in **Opaque** blend mode and 0% in **Alpha** or **Additive** when the app doesn't explicitly request a specific dimming level. System settings or hardware controls can override default dimming behavior. After such an override, the default dimming behavior is ignored until the app restarts. When the Android XR: AR Camera feature is enabled on a supported runtime, your app can request a dimming level through the scripting API.

The Android XR package doesn't currently use the **Additive** blend mode.

> [!NOTE]
> VST devices require **Alpha** blend mode, but on Galaxy XR devices, dimming isn't currently available because the runtime doesn't enable the `XR_ANDROID_global_passthrough_dimming` extension.

#### Automatic blend mode switching

When the Android XR: AR Camera feature is enabled, the Android XR package switches automatically between **Alpha** or **Opaque** blend mode based on passthrough state. 

The Android XR: AR Camera feature enables the blend mode switching behavior. 

Passthrough state is determined by whether the **AR Camera Manager** component is enabled or disabled:
- When the AR Camera Manager component is enabled, the package uses **Alpha** blend mode.
- When the AR Camera Manager component is disabled, the package uses **Opaque** blend mode.

If the Android XR: AR Camera feature is disabled, the Android XR package doesn't enable `XR_ANDROID_global_passthrough_dimming` so dimming requests are unsupported.

> [!NOTE]
> This behavior requires both the Android XR: AR Camera feature and the **AR Camera Manager** component. 

#### Dimming device differences

Dimming behavior can differ between Optical See-Through (OST) and Video See-Through (VST) devices, and depends on the environment blend mode in use.

Support for dimming can vary on both OST and VST devices, depending on whether the device runtime supports the `XR_ANDROID_global_passthrough_dimming` extension.

On OST devices, blend mode behavior can vary between device and runtime. Google Aura devices can apply dimming differently depending on the environment blend mode. The Android XR package sets the blend mode automatically as the passthrough state changes, so dimming results on OST devices depend on multiple factors:
- If passthrough is enabled (via enabling or disabling the **AR Camera Manager component**).
- The resulting environment blend mode, set by the Android XR package.
- The dimming level set by the developer.

VST devices can support dimming if the device runtime supports the `XR_ANDROID_global_passthrough_dimming` extension.

At the time of this package release however, the `XR_ANDROID_global_passthrough_dimming` extension isn't enabled on Galaxy XR devices, so dimming isn't available on those devices.

#### Dimming Best Practices

Dimming works to improve how digital content blends with the physical environment. Use these best practices to make dimming feel comfortable, readable, and appropriate for the user’s surroundings:
- Check supported runtime dimming levels before relying on it.
- Use the lowest dimming level that achieves the desired visual result.
- Avoid abrupt dimming changes; adjust dimming gradually.
- Use dimming only when it improves readability or blending.
- Avoid strong dimming when users need clear awareness of their surroundings.
- Don't rely on dimming alone to communicate important state changes.
- Test dimming on target devices and in different lighting conditions.

#### Example: Read and set dimming levels

The following code sample shows how to use the public dimming APIs to read and request dimming levels in your Unity application:

[!code-cs[read_and_set_dimming_level](../../Tests/Runtime/CodeSamples/DimmingControlSample.cs)]

> [!NOTE]
> When calling `TryRequestGlobalDimmingLevel` the C# API clamps the requested value to `[0,1]` where `0` is no light blocking (least dimming) and `1` is maximum light blocking (most dimming), before forwarding it to the built-in Android XR provider. If the requested value doesn't map exactly to a supported level value, the runtime rounds it to the closest supported level value.

#### Respond to dimming changes

On supported runtimes, your app can subscribe to the dimming-changed event to respond when the dimming level is changed by the system, hardware controls, or other runtime-driven behavior.

Use this event if your app needs to update UI or other behavior when the dimming level changes outside your own dimming requests.

```csharp
void OnEnable()
{
    ARCameraFeature.dimmingLevelChanged += OnDimmingLevelChanged;
}

void OnDisable()
{
    ARCameraFeature.dimmingLevelChanged -= OnDimmingLevelChanged;
}

void OnDimmingLevelChanged()
{
    var currentResult = ARCameraFeature.TryGetGlobalDimmingLevel();
    if (currentResult.status.IsError())
    {
        Debug.LogWarning("Failed to read the current dimming level.");
        return;
    }

    Debug.Log($"Dimming changed: {currentResult.value}");
}
```

## Image capture

This package does not support AR Foundation [image capture](xref:arfoundation-image-capture).

<a id="light-estimation"></a>

## Light estimation permissions

AR Foundation's light estimation feature requires an Android system permission on the Android XR runtime. Your user must grant your app the `android.permission.SCENE_UNDERSTANDING_COARSE` permission before it can track scene data.

To avoid permission-related errors at runtime, set up your scene with the AR Camera Manager component disabled, then enable it only after the required permission is granted.

The following example code demonstrates how to handle required permissions and enable the AR Camera Manager component:

[!code-cs[request_light_estimation_permission](../../Tests/Runtime/CodeSamples/PermissionSamples.cs#request_light_estimation_permission)]

For a code sample that involves multiple permissions in a single request, refer to the [Permissions](xref:androidxr-openxr-permissions) page.
