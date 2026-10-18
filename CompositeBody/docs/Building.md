# Building the PCVR player

Two ways. Both produce the same thing; the menu item is the one to prefer, because it does not
depend on whatever the Build Settings window was last left showing.

## The menu item

**Tools → Build → Windows PCVR (MainScene)**

- builds **only** `Assets/_Scenes/MainScene.unity`, not the Build Settings list
- Windows x64, **development build** — this is what writes `Player.log` and keeps the deep
  profiler available, and it is what you want while testing
- output: `Builds/Windows/CompositeBody.exe`

**Tools → Build → Windows PCVR (MainScene, release)** is the same without the development flag,
into `Builds/Windows-Release`. Use it for the venue: no profiler, no development watermark.

## The Build Settings window

**File → Build Settings** works too, and the scene list is already correct:

| | |
|---|---|
| `Assets/_Scenes/MainScene.unity` | **enabled, first** |
| `_Test/MembraneCombineTest.unity` | off |
| `_Test/CompositeBody_Experience.unity` | off |

Leave the two test scenes off. `CompositeBody_Experience` especially: it is the **legacy beat
spine**, and when the session manager used to load it on top of MainScene it produced two sets of
scans and a timeline bound to the wrong copy of everything.

Check the list has not drifted with **Tools → Verify → Build Settings**. It fails if the first
enabled scene is not MainScene, or if a `_Test` scene has been switched on.

## Copying a build to the other machine

Copy the **whole `Builds/Windows` folder**, not just the `.exe`. The executable is a ~650 KB
launcher; the 360 MB that matters is in `CompositeBody_Data`.

**Both machines must run the same build.** Netcode indexes NetworkBehaviours by their order on a
prefab, so two machines whose player prefabs disagree will connect, spawn, and then misroute
every per-behaviour message — with no error on either side. If something replicates strangely
after a prefab change, rebuild both before looking anywhere else.

## Logs

A development build writes to:

```
%USERPROFILE%\AppData\LocalLow\DefaultCompany\CompositeBody\Player.log
```

Useful lines to search for when something is wrong:

| line | meaning |
|---|---|
| `hand origins resolved` | the owner found something to drive its hands from, and says which |
| `paired 40 finger bones` | Ch36's fingers are following the tracked hand |
| `No XR Rig Available` | the rig was missing at spawn; the retry should recover it |
| `fully arrived` | an avatar finished forming — before any touch, that is a bug |
| `[FormationSync] touch sent` | the touch left the machine |

## Command-line flags

The build is inert without these; they are for testing, not the venue.

| flag | does |
|---|---|
| `-posedebug` | logs every player's head and hand pose, and whether it may transmit, once a second |
| `-solo` | lets one player start and run the whole show (always allowed in the editor) |
| `-autohost` | hosts and reports whether a player spawned, then exits |
| `-probehost` / `-probeclient` | two processes on one machine, to reproduce a two-headset session |

A headless smoke test, which is worth running on a build before carrying it anywhere:

```
Builds\Windows\CompositeBody.exe -autohost -batchmode -nographics -logFile smoke.log
```

Look for `[Smoke] RESULT: PASS`.
