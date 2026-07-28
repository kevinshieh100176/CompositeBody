# Changelog

## [2.0.2] - 2026-03-12

- Updated version to match Dedicated Server Package.

## [2.0.1] - 2025-11-07

### Changed
- Updated minimum engine required version to 6000.3.0b10.

## [2.0.0] - 2025-10-07

### Added
- Added Multiplayer local simulation mode to server local instances
- When deploying remote instances, automatically create a fleet with the correct architecture based on the server's build profile (e.g. if building for ARM64, will automatically create an ARM64 fleet).

### Changed
- This version only supports Unity 6.3 and beyond. 
- Migrated most of the code to the engine into the Play Mode Framework and the Multiplayer modules. This change is laying the foundation for a more stable and open Play Mode experience.
- Fixed an issue where the clone editor’s audio toggle would reset every time scripts were reloaded.
- Updated minimum version of Multiplayer Services packages to 1.2.0-pre.1.


## [1.6.0] - 2025-06-18

### Added
- Extended support for Free Running Instances to include both Local and Remote Instances, enabling users using Play Mode Scenarios to launch them independently of scenario definitions, thereby enabling more flexible work flows and faster iteration speeds.
    - Toggle an Instance's Manual Mode via the Play Mode Status window (Window > Multiplayer > Play Mode Status)
    - Activate or deactivate a Manual mode Instance prior to starting a Scenario to streamline development iteration.
    - Activated or deactivated instances will remain unaffected by Scenario operations - For example, Activated Instances stay launched regardless of Scenario execution.
- Added a safeguard window that prompts users to clean up and terminate active free-running instances when switching scenarios.
- Added ability to quickly view an instance's Run Mode state with Run Mode status icons within the Play Mode Status and Instance Status Dropdown windows.
- Add a button to the Play Mode popup window that opens the Play Mode Status window
 
### Fixed
Fixed an issue where the mouse could still interact with the Scenario Config window while in play mode
Fixed an issue where the "No Play Mode Scenario selected" info message displays twice in the Play Mode Status window.


## [1.5.0] - 2025-06-03

### Fixed
- Fixed pause and step buttons disabled on default play mode configuration.
- Fix step button disabled when scene reload is enabled
- Fixed an issue where entering play mode can result in a visual de-sync of play mode buttons.
- Ensured warnings are shown and that the Run Device options are hidden when an incorrect Android Build Target is chosen for Server Instances.
- Fixed an issue where exiting and re-entiring play mode during the EditorApplication.playModeStateChanged callback was causing unexpected behavior in the play mode scenarios.
- Fixed an issue where the Play Mode Status not update when switching scenario configs from play mode popup content window
- Fixed performance issues related to APK process monitoring on play mode scenarios with Android instances.
- Fixed an issue where launching local instances on Android numerous times consecutively fails with an 'illegal characters in path' error.
- Fix the issue where the launching scenario window UI isn’t ready during the build process

### Added
- Added android mobile support to the Playmode scenarios:
   - An android build profile can now be added in the Scenario Configuration window for local instances
   - This works for both Android based phones and Android based XR devices
-  Implemented support for Free Running Clone Editor Instances, enabling users using Play Mode Scenarios to launch editor instances that are independent of scenario definitions, thereby enabling more flexible work flows and faster iteration speeds. Usage:
    - Toggle an Instance's Manual Mode via the Play Mode Status window (Window > Multiplayer > Play Mode Status)
    - Activate or deactivate a Manual mode Instance prior to starting a Scenario to streamline development iteration.
    - Activated or deactivated instances will remain unaffected by Scenario operations. *For example*, Activated Instances stay launched regardless of Scenario execution.

## [1.4.3] - 2025-05-14

### Fixed
- Using non authorized characters when creating a new Scenario configuration is now returning a warning rather than an error.
- Fixed an issue where toggling Simulator mode in the Virtual Player's Game Window fails to show
- Removed the minimum height restriction on PlayModePopupContentWindow to prevent excessive height when only one or two scenario configs are present
- Fixed low-resolution icons in various windows
- Set a minimum width for the scenario config list in the Scenario Config window to prevent resizing it below a usable size
- Added an info HelpBox that appears when no scenario is selected, preventing the window from appearing blank
- Updated the Multiplayer role dropdown to display “Client And Server” with proper spacing in the Scenario Config window
- Fixed an issue where icons did not adapt when switching between dark and light mode in the editor in PlayModePopupContentWindow and PlayModeStatusWindow

## [1.4.2] - 2025-04-07

### Fixed

- Fixed an issue in Playmode Scenarios where the initially assigned scene was being reloaded a second time in Clone Editors during runtime

## [1.4.1] - 2024-03-27

### Fixed
- Fixed an issue where a "Center On Children" error would appear after adding a Game Object to the Clone Editor's hierarchy during Play Mode.
- Added a scenario name length check to prevent error pop-ups and editor issues when the file name exceeds 64-character limit

### Added
- Added Free Running Instance support for Clone Editors, allowing users to independently run Clone Editors outside of Scenario-based modes

## [1.4.0] - 2025-02-04

### Fixed
- Fixed launching scenario progress bar transition animation.
- Fixed exception when starting a scenario with no instances.
- Fixed the scenario not exiting play mode when stop is requested during a domain reload
- Avoid log spamming with errors when the project is renamed.
- Virtual player audio can now be muted, and mute all players only mutes players at launch
- Fixed issue where deleting Player Tags from Project Settings also removes them from existing Scenarios.
- Fixed issue for Clone Editor instances where editor window controls or widgets don't show.
- Fixed an ArgumentNullException issue that occurs across an MPPM upgrade with an actively set playmode Scenario configuration.
- Fixed an issue where project settings were not getting propagated to Virtual Players when entering Playmode via the play button
- Fixing the issue where the launching scenario window persists when domain/scene reload is enabled in the project settings.
- Virtual players can now be muted and mute all virtual player setting only mutes players at launch
- Fixed an issue where entering and exiting Playmode quickly can cause virtual player window's Playmode state to get out of sync.
- Removed use of obsolete internal build profile API functions
- Fixed an issue where clone editor VP Windows closes unexpectedly when entering a Playmode Scenario with Multiplayer Windows opened.

### Changed

- Moved the warning helpbox in MPPM window to the bottom of the VP list during VP activation.

### Added

- Added `CurrentPlayer.IsMainEditor` to the API to check if the current player is the main editor instance.
- Added a main editor label to scenario launch window
- Added a scenario launch window to show the scenario stage, progress bars, and messages.
- Added a new Entities Hierarchy window for clone editor VP Windows where it can be toggled under Layout > "Entities Hierarchy".

## [1.3.3] - 2024-12-17

### Added

Fixed an ArgumentNullException issue that occurs across an MPPM upgrade with an actively set playmode Scenario configuration.

### Fixed

Fixed an issue where project settings were not getting propagated to Virtual Players when entering Playmode via the play button

## [1.3.2] - 2024-11-14

### Fixed
- Fixed clones remaining active when a play mode scenario is stopped while the clone is activating.
- Fixed an issue where enabling Playmode Tools in a clone editor's layout configuration may display incorrect layouts.
- Fixed compilation error "'UnityPlayer' does not contain a definition for 'Role'" when Dedicated Server package version doesn't match.

### Added
- Avoid reconstructing virtual player windows through Playmodes if their layouts in PlayMode vs EditMode are the same. This helps retain anchoring in Windows.

## [1.3.1] - 2024-10-21

### Fixed

- Fixed an issue where the "Player X failed to sync" window would pop up even though no synchronization issue could be observed.
- Virtual Player folder is now properly refreshed when the package, engine, or engine changeset version is updated.

### Added

- Added a helpbox to the Multiplayer window to prevent package imports while the virtual player(s) are activating. The helpbox will remain visible until all virtual player(s) are ready.
- Added Multiplayer Menu links to the scenario configuration and the Scenarios status under Windows > Multiplayer

## [1.3.0] - 2024-09-26

### Changed

- Updated `com.unity.services.multiplayer` to 1.0.0 release version.
- Bump of the minimum Engine version to 6000.0.22f1 to address a crash happening when using virtual players with a build profile.

## [1.3.0-pre.3] - 2024-09-20

### Changed

- Replaced the **Original Name** property in the **Remote Instances** > **Advance Configuration** section with the **Identifier** property. The **Identifier** string field produces a unique name for the Multiplay Build, Build Configuration, and Fleet. This name is in the form `CreatedFromTheUnityEditor-[identifier]-[username]`.

### Added

- Added stream logs to main editor option for additional editor instances
- Fixed streaming logs from local instances stopping when domain reloads are enabled
- Added percentage progress to the Playmode Status Window for the Preparing, Deploying, and Launching stages

### Fixed

- Removed compilation warnings "This async method lacks 'await' operators and will run synchronously"
- Fixed streaming logs from remote instances not working.
- Fixed code changes not consistently syncing properly between the main editor and the clone when using Rider
- Fixed the asset out of sync error caused by the clone not starting in the correct build target

## [1.3.0-pre.2] - 2024-08-14

### Fixed
- Fixed MPPM virtual player exiting play mode by calling OpenScene() and CloseScene() during play mode errors when multiple scenes are present/switching in the project.
- Fixed "type is not a supported string value" error when adding a tag to the main editor instance
- Fixed tags not persisting in the tag dropdown after navigating away from the scenario config window
- Fixed  Mppm can't spawn virtual player in playmode

### Changed
- MPPM Window is now disabled when there is a scenario actively selected in the dropdown

## [1.3.0-pre.1] - 2024-07-26

### Changed
- Improved time it takes to enter play mode with remote instances on consecutive runs when they produce equivalent build files.
- Main editor instance is now optional.
- Updated the remote deployment dependency to `com.unity.services.multiplayer@1.0.0-pre.1`
- Removed UPM from the clones to improve performance and so clones rely on library redirect as originally intended

### Fixed
- Added a modal offering “Save,” “Don’t Save,” and “Cancel” options when initiating a virtual player with an unsaved scene
- Fixed Asset database out of sync error when launching a clone

## [1.3.0-exp.4] - 2024-07-16

### Changed
- Updated the remote deployment dependency to `com.unity.services.multiplayer@0.6.0`

## [1.3.0-exp.3] - 2024-07-11

### Changed
- Moved the Multiplayer Window menu item from "Window/Multiplayer Playmode" to "Window/Multiplayer/Multiplayer Playmode" to ensure consistency with the other Multiplayer packages.
- Scenario status popup title changed from "Connection Status" to "Instances Status".

### Added
- Added a new Playmode Status Window that gives more information about each instance in the scenario

### Fixed
- Removed warning log when the multiplay package is not installed. Information message now appears only in the UI in the "configure play mode scenarios" window.
- Fixed remote instances default arguments from `-log` to `-logFile`.

## [1.3.0-exp.2] - 2024-07-03

### Fixed
- Fixed "Stream logs to main editor" option not working in local instances.

## [1.3.0-exp.1] - 2024-06-24

### Added
- Extending Playmode with scenarios configuration capabilities. Allowing orchestration of in editor, local and remote instances.

### Fixed
- No longer hit an exception when launching the standalone profiler

## [1.2.0] - 2024-06-04

### Added
- Added tool tips to the preferences window
- Flatten the MPPM preferences window, also changed first checkbox wording

### Fixed
- Fixed Multiplayer roles not displaying after reinstalling Dedicated Server Package
- Fixed main view window, so it may not be shrunk destructively
- Fixed the tooltip auto-close issue, so it now persists long enough to be useful.


## [1.1.0] - 2024-04-24

### Added
- Added Netcode For Entities layout option to each mppm clone if package is installed.
- Added the ability to mute players to the settings
- Added the ability to change the asset database refresh timeout to the settings
- Can now properly focus on other individual player windows from inside an individual player (by using the keyboard shortcuts)
- Added clearer message to certain types of Symlink failures (on FAT32) which are not supported

### Fixed
- Upgrades of the MPPM package now clear out the local clone cache to ensure stable updates
- Fixed a crash with layout files when computers were set to certain regions
- Changed default multiplayer role of player clones to Client and Server
- Added a minimum width for the main view of the MPPM window
- Escape key no longer closes virtual player windows on Windows
- Fixed issues with heartbeat timeout
- No longer allow '/' as part of tags as these types of slashes are intended for drop down behavior

## [1.0.0] - 2024-03-12

### Added

- Multiplayer development workflow aiming to offer a more efficient and quick development cycle of multiplayer games. The tool enables opening multiple Unity Editor instances simultaneously on the same development device, using the same source assets on disk.
