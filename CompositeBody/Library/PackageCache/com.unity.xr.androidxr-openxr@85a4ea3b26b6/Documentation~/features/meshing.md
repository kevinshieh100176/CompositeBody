---
uid: androidxr-openxr-meshing
---
# Meshing

This page supplements the AR Foundation [Meshing](xref:arfoundation-meshing) manual. The following sections only contain information about APIs where Google's Android XR runtime exhibits platform-specific behavior.

[!include[](../snippets/arf-docs-tip.md)]

## Use meshing in a scene

To get started with meshing with AR Foundation, follow the instructions in AR Foundation's [Use meshing in your scene](xref:arfoundation-meshing-use).

## Mesh classification

This package maps Android XR's native object trackable label component to AR Foundation's [Mesh Classifications](xref:UnityEngine.XR.ARSubsystems.XRMeshClassification).

Refer to the table below to understand the mapping between AR Foundation's classifications and Android XR's labels:

| AR Foundation Label | Android XR label |
| :------------------ | :--------------- |
| Unknown             |                  |
| Other               | OTHER            |
| Floor               | FLOOR            |
| Ceiling             | CEILING          |
| Wall                | WALL             |
| Table               | TABLE            |
| Seat                |                  |
| Window              |                  |
| Door                |                  |

> [!NOTE]
> A blank label indicates that Android XR doesn't support the corresponding mesh classification.

## Optional feature support

The following table indicates which meshing features Android XR supports:

| **Feature** | **Supported** |
| :---------- | :-----------: |
| Density | |
| Normals | Yes |
| Tangents | |
| Texture coordinates |
| Colors | |
| Queue size | Yes |
| Classification | Yes |

Refer to [AR Mesh Manager](xref:arfoundation-meshing-manager) (AR Foundation) to learn more about AR Mesh Manager features.

## Sample scenes

AR Foundation provides three meshing sample scenes to demonstrate meshing features. To learn more about these meshing samples, refer to [Meshing samples](xref:arfoundation-samples-meshing).

## Additional information

* [Android XR scene meshing](https://developer.android.com/develop/xr/openxr/extensions/XR_ANDROID_scene_meshing#XrSceneMeshSemanticLabelANDROID) (Android developer documentation).
