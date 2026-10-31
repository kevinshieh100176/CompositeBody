# CompositeBody

Unity 6000.5.5f1 PCVR project. The Unity project root is `CompositeBody/`; the repo root holds `Tools/` and `dev/`.

## Unity work goes through the Unity CLI

`unity` (Unity CLI, on PATH) is the default way to do anything in the editor. `com.unity.pipeline` is in the manifest, so a running editor exposes ~160 commands on 127.0.0.1:7800.

Open the editor once per session, then drive it:

```
unity projects open . --until-ready --timeout 900 --json --no-banner --non-interactive
unity status                     # port / state / project / version / pid of connected editors
unity recompile                  # recompile and print the compile-error table
unity list                       # what this editor exposes
unity cmd editor_status --json   # ready / compiling / playMode / domainReloadInProgress
unity cmd console --json         # captured log — do not tail Logs/Editor.log
```

Useful commands: `console`, `console_status`, `recompile`, `eval`, `run_tests`, `capture_game_view`, `capture_scene_view`, `find_gameobjects`, `bake_lighting`, `build`, `batch` (several ops in one request). `unity job` detaches a long one.

**Do not reach for `Unity.exe -batchmode` unless no editor is open and one must not be opened.** It pays a multi-minute cold start per call and is verified by grepping a large log.

### Never drive these against an editor somebody is using

The play-mode harnesses enter play mode and let their runner call `EditorApplication.Exit`, which closes the editor out from under whoever is sitting in front of it. They need a headless `-batchmode` run (launched **without** `-quit`) with the editor closed:

- `RunO1PayoffTest`, `RunInteractionDebugTest`, `RunBeatSpineTest` — their runners exit unconditionally
- `CaptureVFX` / `VFXCaptureRunner` — same
- `RunMembraneAvatarTest` is the exception: `MembraneAvatarTestRunner` guards the exit with `Application.isBatchMode` and otherwise just leaves play mode. Copy that guard into the others before running them live.

Separately, most editor tools here call `EditorSceneManager.OpenScene(..., OpenSceneMode.Single)` — every `Build*`, `RenderOnboardingStills`, `VerifyMainScene`, `RepairO0`. They replace the open scene, so save first.

### Reading results

Judge a CLI run by the JSON `success` field, never by the process exit code. Wrapping the CLI in a shell line that ends with `echo exit=$?` reports 0 while the CLI itself exited non-zero with `"success": false`.

Compile errors do not fail an open loudly: the editor stops on "Enter Safe Mode?" and the JSON carries `ready.outcome: "dialog"` with `OPEN_BLOCKED_BY_DIALOG_SAFE_MODE`. The errors are in `Logs/Editor.log` (`grep -E "error CS[0-9]+"`, repeated once per assembly pass). An editor parked on a modal dialog has no graceful-close channel, so `unity projects close .` returns `PROJECTS_CLOSE_UNREACHABLE` and needs `--force`.

`Temp/UnityLockfile` existing does not mean an editor is open — it is often stale. `AppData\Local\Unity\bin\unity.exe` is the CLI; an editor holding the project runs from `Program Files\Unity\Hub\Editor\`.

## Interactions must be debuggable

Every new interaction implements `IDebugInteraction` and reports its gate, or it is not finished. See `Assets/Scripts/Diagnostics/`.
