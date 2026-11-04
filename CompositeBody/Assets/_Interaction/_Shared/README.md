# _Shared — interaction modules' common core

Used by `Assets/_Interaction/Home/` and `Assets/_Interaction/ObjectAssembly/`. Compiles into
Assembly-CSharp / Assembly-CSharp-Editor (no asmdef). Module sessions must not edit this folder;
ask the coordinating session for changes.

## Runtime (`Scripts/`, namespace `CompositeBody.Interaction`)

### `NetGrabbable` (abstract `NetworkBehaviour`, `IXRSelectFilter`, `IXRHoverFilter`)
Server-granted grab ownership for client-server Netcode. Put on the fragment ROOT next to
`XRGrabInteractable` + `ClientNetworkTransform` (use `InteractionBuildUtil.ConfigureGrabbable`).

| Member | |
|---|---|
| `const ulong NoHolder` | `ulong.MaxValue` (0 is the host) |
| `ulong holder`, `bool isHeld` | replicated, server-written |
| `bool heldLocally`, `bool awaitingGrant` | this machine |
| `XRGrabInteractable interactable` | |
| `protected virtual bool reclaimOnRelease` | default true: server takes ownership back + teleports to the release pose |
| `protected abstract bool ServerCanGrant(ulong sender)` | holder is already free when asked |
| `protected virtual void ServerOnGranted(ulong sender)` | |
| `protected virtual void ServerOnReleased(ulong sender, Vector3 pos, Quaternion rot)` | holder already cleared |
| `protected virtual void ServerOnHolderLost(ulong client)` | holder disconnected |
| `protected virtual bool LocalCanSelect()` | extra local gate (e.g. state == Free) |
| `protected virtual void OnLocalGrabStarted() / OnLocalGrabEnded()` | also fire with no session |
| `protected virtual void OnHolderChanged(ulong prev, ulong cur)` | every machine |
| `protected virtual bool NetworkTransformWanted()` | return false while animating locally (CNT stays off) |
| `protected void DropLocally()` | cancel this machine's selection, no RPC |
| `protected void ServerClearHolder()` | for place / vanish / reset |
| `protected void ServerReclaimOwnership(float delaySeconds = 0)` | delay so a state change reaches clients first |
| `protected void ServerSetPose(Vector3, Quaternion)` | teleport when authority, else set transform |
| `protected virtual void Update() / LateUpdate()` | call `base.` when overriding |

Protocol: local select → (non-owner: CNT off) → `RequestHoldRpc` → server grants (holder=sender,
ChangeOwnership) or `DenyHoldRpc` → loser drops. Release is detected in `LateUpdate`
(hand-switch safe) → `ReleaseHoldRpc(pos, rot)`. Holder disconnect clears holder.
Logs `[NetGrabbable] ... select refused` on refusal.

### `BreathingView` (`MonoBehaviour`, on the fragment's VISUAL CHILD)
`breathing` (target 0..1, eased), `breathingNow`, `shake01` (set per frame), `reveal01` (0 =
renderers off; writes `_Reveal`), `Wobble(float sharedTime)`, `CancelWobble()`, `SnapBreathing()`.
Subclass hook: `protected virtual void Compose(float now, ref Vector3 offset, ref Quaternion turn, ref float scale)`.
Emission pulse needs a material with `_EMISSION` on (`InteractionBuildUtil.MakeFragmentMaterial`).
Uses one MPB; never touches shared materials. Phase from `ExperienceClock.PhaseFor(parent position)`.

### `InteractionEasing` (static)
`EaseIn(t, curve=null)` (cubic), `EaseInOut(t, curve=null)`, `Wobble(sinceArrival, duration, freq=7)`
→ [-1,1] damped sine, `Shake(time, freq, seed)` → Perlin Vector3 [-1,1], `Remap01(v, from0, from1)`.

### `InteractionPhaseClock` (static)
`Unset = -1`, `now` (double server time / local fallback), `IsSet(stamp)`, `Since(stamp)`.
Use `NetworkVariable<double>` stamps written by the server.

### Diagnostics (`Scripts/Diagnostics/`, namespace `CompositeBody.Interaction.Diagnostics`)
- `InteractionTestHand : XRBaseInteractor` — `Create(name, pos)`, `TryGrab(grab)` (validated, filters
  apply → false = refused), `ForceGrab(grab)`, `Release()`, `MoveTo(pos)`.
- `InteractionTestLog` — `Check(ok, what)`, `WaitFor(cond, timeout, what)` (IEnumerator), `Finish()`
  logs `[Tag] RESULT: PASS/FAIL`; exits the editor ONLY in batch mode, otherwise leaves play mode.
- `DesktopGrabProbe` — G grabs the NetGrabbable in view (for MPPM virtual players, no XR).

## Shader (`Shaders/InteractionReveal.shader`, `CompositeBody/InteractionReveal`)
URP Lit clone (forward pass = URP's own `LitForwardPass.hlsl`) + reveal:
`_Reveal` (0 gone → 1 whole; UV-noise clip in every pass), `_RevealNoiseScale`, `_RevealEdgeWidth`,
`_RevealEdgeColor` (default black — surfaces arrive out of black), `_RevealEdgeDarken`,
`_RevealEdgeGlow`, `_SourceAlphaClip` (1 = keep the source's texture-alpha cutout).
**Every material on it must have `_ALPHATEST_ON`** (shadow/depth passes only clip under it).
Verified: all 9 passes compile with `_ALPHATEST_ON _EMISSION _NORMALMAP`.

## Editor (`Editor/InteractionBuildUtil.cs`, namespace `CompositeBody.Interaction.EditorTools`)
- `RequireCleanActiveScene(path, tag, out scene)`, `AnySceneDirty(tag)` — never save for the user.
- `Check(tag, ok, what, failures)`, `Verdict(tag, failures)`.
- `ConfigureGrabbable(root, colliderMesh)` → `XRGrabInteractable` (all flags set; then add your
  NetGrabbable subclass), `VerifyGrabbable(tag, root, failures)`, `GetOrAdd<T>(go)`.
- `RegenerateNetworkObjectHashes(scene)` (after the first save), `VerifyHashes(tag, scene, failures)`, `HashOf(netObj)`.
- `MakeRevealTwin(srcMat, folder, revealDefault=1)` → saved `<src>_Reveal.mat`;
  `MakeFragmentMaterial(srcMat, path)` (emission on, black); `IsRevealMaterialOk(mat)`; `RevealShaderOk(out problem)`.
- `ClearBatchingStatic(root)` (keeps ContributeGI), `AnyBatchingStatic(root)`.
- `BuildDemoBootstrap(scene, p1SpawnPose, p2SpawnPose, tag)` → `--- Demo Bootstrap ---` root
  (Network Manager, XRI Network Game Manager, notification UI, GameSessionManager with
  `m_ExperienceSceneName=""` and solo allowed, StaffControlPanel + spawns, InteractionDebugPanel +
  Roster, Playspace + XRMPT rig, CalibrationPoint, DesktopGrabProbe); deactivates other scene cameras.
  `VerifyDemoBootstrap(tag, scene, failures)`.

Fragment stack set by `ConfigureGrabbable`: convex MeshCollider · NetworkObject
(DontDestroyWithOwner, no AutoObjectParentSync) · kinematic Rigidbody, no gravity, no interpolation ·
ClientNetworkTransform (pos/rot, world) · XRGrabInteractable (Instantaneous, Single, dynamic attach,
no throw, no gravity on detach, `unparentTransformOnGrab=false`, `predictedVisualsTransform=null`,
ease-in 0).
