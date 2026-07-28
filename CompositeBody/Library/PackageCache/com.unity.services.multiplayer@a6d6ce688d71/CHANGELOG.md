# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [2.2.4] - 2026-06-17

### Fixed

- Fixed session properties not reflecting the server response after `SavePropertiesAsync()` until `RefreshAsync()` was called explicitly.
- `WrappedLobbyService.TryCatchRequest` no longer throws a `NullReferenceException` when `HttpException<ErrorStatus>.ActualError` is null (empty-body HTTP error). Falls back to lifting the HTTP status into the lobby enum range with `Unknown` as the last resort.

## [2.2.3] - 2026-05-19

### Changed
- Dependency update: Bump `com.unity.services.wire` to version 1.4.4 to fix wire failing to connect on WebGL when using multiple UnityServices instances.

## [2.2.2] - 2026-05-07

### Added
- `Logger` accepts `UnityEngine.Object` as context.

### Changed
- `SessionConnectorBehaviour` emits warning messages with context. It allows to easily identify which gameobject is responsible for the warning by clicking on the warning message in the editor's console. Once clicked, it will highlight the gameobject which issued the warning message.

### Fixed 
- Fixed an issue where the connection task could be cancelled prematurely when an internal timeout was reached during session creation; session creation now correctly relies on the timeouts defined by NetworkManager when used with Netcode for GameObjects.
- Ensure the `SessionConnectorBehaviour` is no longer throwing `NullReferenceException` when `SessionConnector`'s multiplayer session is `null`.
- Fixed `SessionConnectorBehaviour`'s clean-up so it no longer throws `NullReferenceException`.
- Fixed `SessionConnectorBehaviour`'s inspector.
- Fixed `AddingSessionStarted` and `SessionAdded` not firing.

## [2.2.1] - 2026-04-13

### Fixed
- Fixed compilation errors when making builds caused by including editor-only code in runtime-only code.
- Use the Netcode for GameObjects timeouts for connecting and synchronizing the NetworkManager during session creation.

## [2.2.0] - 2026-03-31

### Added
- Multiplayer Session ScriptableObject to hold an ISession and expose the session events to the inspector.
- Session Connector ScriptableObject to populate the Multiplayer Session ScriptableObject. Create, Join and CreateOrJoin connectors are available they include session settings as well as network settings.
- Session Connector Wrapper Monobehaviour to automatically execute the specified connector on sign in.
- New verbose settings in `Project Settings > Services > Multiplayer`.

### Changed
- `ISession` created or joined through the `MultiplayerService` in playmode in the editor are automatically left when exiting playmode.

### Fixed
- Dependency update: Bump `com.unity.services.deployment` to version 1.7.1 to pick up an editor compatibility fix (InstanceID deprecation).
- Fixed a bug in the sessions API where if using direct connections and Netcode for GameObjects 2.8 or later, an erroneous endpoint would be used for the connection data, leading to errors about an "invalid listen endpoint" followed by an endpoint with the port duplicated.
- Exiting playmode in the Editor when a Session is connected with Netcode for GameObjects' `NetworkManager` doesn't log warnings anymore.
- Fixed wrong Exception handling in the Matchmaker module and the `NetworkMetadata` handler when joinning a `ISession` already connected to a `NetworkManager`.
- Fixed an issue where matchmaker queue deployment could fail when the queue contains filtered pools.
- Fixed player data change event handling.

## [2.1.2] - 2026-02-13

### Added
- Support for Matchmaker Multiplay and CloudCode hosting types edition in the inspector.
- Support pull and push Multiplay and CloudCode hosting types from/to Matchmaker.

### Fixed
- Fixed `.mmq` with match hosting types different than MatchId failing to be imported in Unity.
  
## [2.1.1] - 2026-01-29

### Fixed
- Fixed an issue where using an invalid `RelayProtocol` in `SessionOptions.WithNetworkOptions` would attempt to connect with the incorrect protocol.

## [2.1.0] - 2026-01-26

### Added
- Introduced `SessionOptions` and `JoinSessionOptions.WithNetworkOptions(NetworkOptions options)` API to allow specific per-client session settings during session creation and joining.
- `NetworkOptions` setting includes a `RelayProtocol` field, enabling clients to specify their `RelayProtocol` when connecting to Relay or Distributed Authority.
  - Note: This setting takes precedence over any `RelayProtocol` value passed to a `WithRelayNetwork` or `WithDistributedAuthority` option.
- Added methods in `CreateBackfillTicketOptions` for setting assignment information for backfilling.
- Added `CreateSessionAsync` method in `IMultiplayerServerService` to create a session with a provided session ID for easy local iteration.
- Added `CreateMatchSessionAsync` method in `IMultiplayerServerService` to create a session from a matchmaker match ID.

### Changed
- The `RelayProtocol` value set in `SessionOptions.WithRelayNetwork` and `SessionOptions.WithDistributedAuthority` is obsolete, replaced by the new `.WithNetworkOptions` API.
- The `CreateAssetWithTextContent` method replaces the `CreateAssetWithContent` method starting from version `6000.4`.

### Fixed
- Fixed issues with Matchmaker configuration `mmq` file deployment, which failed when a relaxation rule didn't have a value, even if it was valid.
- Fixed issues with the Matchmaker configuration `mmq` file inspector, which could lose modified values when serialization failed after pressing `Apply`.
- Fixed issue where matchmaking in peer-to-peer would result in rate limit errors.

### Removed
- Removed Multiplay Hosting integration and associated Matchmaker APIs. Support for Unity Multiplay Hosting was removed in version `2.0.0` and is not available in subsequent releases.

## [2.1.0-exp.1] - 2026-01-19

### Added
- Introduced `SessionOptions` and `JoinSessionOptions.WithNetworkOptions(NetworkOptions options)` API to allow specific per-client session settings during session creation and joining.
- `NetworkOptions` setting includes a `RelayProtocol` field, enabling clients to specify their `RelayProtocol` when connecting to Relay or Distributed Authority.
  - Note: This setting takes precedence over any `RelayProtocol` value passed to a `WithRelayNetwork` or `WithDistributedAuthority` option.
- Added methods in `CreateBackfillTicketOptions` for setting assignment information for backfilling.
- Added `CreateSessionAsync` method in `IMultiplayerServerService` to create a session with a provided session ID for easy local iteration.
- Added `CreateMatchSessionAsync` method in `IMultiplayerServerService` to create a session from a matchmaker match ID.

### Changed
- The `RelayProtocol` value set in `SessionOptions.WithRelayNetwork` and `SessionOptions.WithDistributedAuthority` is obsolete, replaced by the new `.WithNetworkOptions` API.

### Fixed
- Fixed issues with Matchmaker configuration `mmq` file deployment, which failed when a relaxation rule didn't have a value, even if it was valid.
- Fixed issues with the Matchmaker configuration `mmq` file inspector, which could lose modified values when serialization failed after pressing `Apply`.

## [2.0.0] - 2025-11-13

### Added
- Added a new `Fetch from Remote` command for local matchmaker queue and environment files in the Deployment Window, allowing local configuration to be overridden by Cloud Dashboard configuration content.

### Changed
- Multiplay Hosting `Deploy` and `Build` commands in the Deployment Window now restore the Editor BuildTarget and/or BuildProfile once the build is complete.

### Fixed
- Fixed `m_NetworkManager` not found in context error in pre-6.0 editors.
- Fixed an issue where Matchmaker configuration assets would not deploy with their latest changes from the Deployment Window.
- Fixed duplicated SessionChanged events.
- Fixed Lobby events being raised before re-subscribing when joining the same Lobby again.
- Fixed `SessionOption.WithRelayNetwork` to ensure that clients joining an existing session with networking always use the specified protocol instead of `RelayProtocol.Default`. If the option is not used, joining defaults to `RelayProtocol.Default`.
- Fixed the DeploymentWindow error message when trying to sync a Matchmaker Queue configuration with an invalid remote file that cannot be parsed.
- Fixed Multiplay Hosting configuration `Deploy` and `Build` commands to prevent the `Cannot start a build from within the playerloop` error.
- Fixed an asset loading error in the `.gsh` file observer that would trigger the first time a project exits the `Safe Mode` status.

## [1.2.0] - 2025-10-15

### Added
- Added Lobby migration data methods.
- Increased Lobby max player count to 150.
- Added migration data methods to `LobbyHandler` and `IHostSession`.
- Added Matchmaker Onboarding Section and presets for Multiplayer Center.
- Added support for Host Migration in lobbies:
  - Added migration operations `GetMigrationDataInfoAsync`, `DownloadMigrationDataAsync`, and `UploadMigrationDataAsync`.
- Added support for Host Migration in sessions:
  - Added `WithHostMigration` session option to enable automatic netcode snapshots at a configurable interval.
    - It requires an implementation of `IMigrationDataHandler` which defines how data is generated and applied.
    - A default migration data handler implementation is provided for Netcode with Entities (Minimum required version 1.7.0).
  - Added migration data methods to `IHostSession`: `GetHostMigrationDataAsync` and `SetHostMigrationDataAsync` for manual implementations.
  - Added host migration flow to restart the network when the session host changes.
  - Added `SessionHostChanged` and `SessionMigrated` events on `ISession`.
  - Added an optional parameter `preserveRegion` to `RelayOptions` to configure relay reallocation behavior during host migration. Setting this to true saves the region of the first relay allocation and reuses it when a relay server is reallocated during host migration.
- Added concurrency control settings to the lobby service and sessions. When enabled, an [If-Match](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/If-Match) header will be sent, and an exception will be thrown in case of conflict for the following operations:
  - Deleting a lobby or session.
  - Removing a player from a lobby or session.
  - Updating a lobby or session player.
  - Updating a lobby or session.
- Added `SessionObserver` class to listen to `ISession` lifecycle for a specific `ISession.Type`.
- Added `AddingSessionStarted` and `AddingSessionFailed` events inside `IMultiplayerService`.
- Added overloads for `WithRelayNetwork` and `WithDistributedAuthorityNetwork` to enable manual setting of the underlying network protocol. Defaults remain the same: Most platforms use DTLS as a default connection, and WebGL still defaults to WSS. Use the `RelayNetworkOptions` variant to override default behavior.
- Added a `WithDirectNetwork` overload that accepts `DirectNetworkOptions`, taking `ListenIpAddress` and `PublishIpAddress` parameters.
- Added a `WithDirectNetwork` overload that accepts no arguments, ensuring backward compatibility with the previous `WithDirectNetwork` overload.
- Added player name integration into multiplayer sessions:
  - `WithPlayerName()` session option for a player to provide their name in a multiplayer session.
  - `GetPlayerName()` extension method to the session `IReadOnlyPlayer` model to retrieve a player's name.
- Added `IsServer` property in `ISession` to validate if the local owner of the session handle is a server managing the session.
- Added `HasPlayer` method in `ISession` to easily validate if a player is in a session.
- Added `GetPlayer` methods in `ISession` & `IHostSession` to easily access a specific player model by player ID.
- The default network handler implementation for netcode for entities will automatically create client & server worlds if none are available when starting a network connection.
- Added an Inspector for Matchmaker queue files to allow editing the most common properties of the Matchmaker Queue configuration in the editor.
- Added `Network` property to provide control over the network managed by the Session.
  - `IHostSession` provides the `IHostSessionNetwork` interface, allowing control over the network connection for the session.
  - `ISession` provides the `IClientSessionNetwork` interface, allowing access to the network state and relevant events.
  - Enter and exit games within the same multiplayer session.
  - Wait for specific conditions before starting the network connection and gameplay:
    - Session reaching max players.
    - All players marking themselves as ready through player properties.
    - Etc.
- Added `Network` property on `ISession` (`IClientSessionNetwork`) & `IHostSession` (`IHostSessionNetwork`) to provide control over the network managed by the Session. This allows entering and exiting games within the same multiplayer session.
- Added parameter validation for `IMultiplayerServerService.StartMultiplaySessionManagerAsync`. An `ArgumentNullException` will be thrown if the following values are not provided:
  - `MultiplaySessionManagerOptions`.
  - `MultiplaySessionManagerOptions.SessionOptions`.
  - `MultiplaySessionManagerOptions.MultiplayServerOptions`.
- Added a new `MatchmakerQueueAsset` class created when importing `.mmq` files, which can be referenced from runtime assets.
- Added a new `BuildProfile` reference and `BuildOptions` settings to the `.gsh` file description to allow Multiplay Server Hosting build deployment to perform more specific builds.
- Added an Inspector for `.gsh` files to allow editing the most common properties of the Multiplay configuration in the editor.
- Custom inspector for `.gsh` files.

### Changed
- Changed soft dependency to the Services Deployment API in the QuickStart from `com.unity.deployment.api@1.1.0` to `com.unity.deployment@1.4.1`.
- Attempting to use an unsupported connection protocol on WebGL will throw an `ArgumentException` instead of logging a warning message.
- Matchmaker queue and environment configuration files will display their status against the remote configuration in the cloud project from the Deployment Window.
- Changed `IMultiplaySessionManager.StartMultiplaySessionManagerAsync` to await the full allocation and session initialization flow and cover all potential errors.
- Removed exception thrown when trying to access the session property in `IMultiplaySessionManager.Session`, as full initialization is now guaranteed.
- Removed error logging for unknown Lobby patch paths over Wire to support future Distributed Authority state updates.

### Fixed
- Creating a new Matchmaker Queue from the QuickStart menu no longer logs a path warning issue.
- Fixed first-time Matchmaker queue file deployment that would leave the Matchmaker disabled if no Matchmaker Environment file was deployed along with the queue files.
- Resolved an issue where a host could not rejoin the same session after leaving while using Distributed Authority.
- Resolved an issue where a rule without a reference could not be deployed.
- Fixed assets being loaded despite being of the incorrect type.
- Fixed stale data used when deploying `.gsh` files via the deployment window.
- Resolved an issue where the `Task` would resolve before the `NetworkManager` was fully connected and synchronized when using Netcode for GameObjects.
- Resolved an issue where the `Task` would resolve before the `NetworkManager` had finished shutting down when using Netcode for GameObjects.
- Resolved an issue where some settings on the `NetworkManager` would remain changed after leaving a Distributed Authority session.
- Fixed an issue where joining clients to a relay-hosted game (either with Relay or Distributed Authority) could not connect due to an invalid relay protocol for their platform. The RelayProtocol from the `_session_network` property is now ignored by joining and reconnecting players. Players will always use the `RelayProtocol.Default` value instead.

## [1.1.8] - 2025-09-09

### Fixed
- Resolved an issue where a rule without a reference could not be deployed.

## [1.1.7] - 2025-08-19

### Changed
- The user-provided allocation callback is now guaranteed to be invoked regardless of whether the session is created successfully.
  - If session creation fails, the `OnAllocate` callback will still be invoked. This means you must check whether the Session is null before using it. The deallocation callback will be invoked once the server times out.
- The user-provided deallocation callback is now guaranteed to be invoked.

### Fixed
- Resolved an issue where the server, upon failing to create a session, would be left in a broken state.

## [1.2.0-pre.1] - 2025-08-06

### Added
- Added support for Host Migration in lobbies:
  - Added migration operations `GetMigrationDataInfoAsync`, `DownloadMigrationDataAsync`, and `UploadMigrationDataAsync`.
- Added support for Host Migration in sessions:
  - Added `WithHostMigration` session option to enable automatic netcode snapshots at a configurable interval.
    - It requires an implementation of `IMigrationDataHandler` which defines how data is generated and applied.
    - A default migration data handler implementation is provided for Netcode with Entities (Minimum required version 1.7.0).
  - Added migration data methods to `IHostSession`: `GetHostMigrationDataAsync` and `SetHostMigrationDataAsync` for manual implementations.
  - Added host migration flow to restart the network when the session host changes.
  - Added `SessionHostChanged` and `SessionMigrated` events on `ISession`.
  - Added an optional parameter `preserveRegion` to `RelayOptions` to configure relay reallocation behavior during host migration. Setting this to true saves the region of the first relay allocation and reuses it when a relay server is reallocated during host migration.
- Added concurrency control settings to the lobby service and sessions. When enabled, an [If-Match](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/If-Match) header will be sent, and an exception will be thrown in case of conflict for the following operations:
  - Deleting a lobby or session.
  - Removing a player from a lobby or session.
  - Updating a lobby or session player.
  - Updating a lobby or session.
- Added `SessionObserver` class to listen to `ISession` lifecycle for a specific `ISession.Type`.
- Added `AddingSessionStarted` and `AddingSessionFailed` events inside `IMultiplayerService`.
- Added overloads for `WithRelayNetwork` and `WithDistributedAuthorityNetwork` to enable manual setting of the underlying network protocol. Defaults remain the same: Most platforms use DTLS as a default connection, and WebGL still defaults to WSS. Use the `RelayNetworkOptions` variant to override default behavior.
- Added a `WithDirectNetwork` overload that accepts `DirectNetworkOptions`, taking `ListenIpAddress` and `PublishIpAddress` parameters.
- Added a `WithDirectNetwork` overload that accepts no arguments, ensuring backward compatibility with the previous `WithDirectNetwork` overload.
- Added player name integration into multiplayer sessions:
  - `WithPlayerName()` session option for a player to provide their name in a multiplayer session.
  - `GetPlayerName()` extension method to the session `IReadOnlyPlayer` model to retrieve a player's name.
- Added `IsServer` property in `ISession` to validate if the local owner of the session handle is a server managing the session.
- Added `HasPlayer` method in `ISession` to easily validate if a player is in a session.
- Added `GetPlayer` methods in `ISession` & `IHostSession` to easily access a specific player model by player ID.
- The default network handler implementation for netcode for entities will automatically create client & server worlds if none are available when starting a network connection.
- Added an Inspector for Matchmaker queue files to allow editing the most common properties of the Matchmaker Queue configuration in the editor.
- Added `Network` property to provide control over the network managed by the Session.
  - `IHostSession` provides the `IHostSessionNetwork` interface, allowing control over the network connection for the session.
  - `ISession` provides the `IClientSessionNetwork` interface, allowing access to the network state and relevant events.
  - Enter and exit games within the same multiplayer session.
  - Wait for specific conditions before starting the network connection and gameplay:
    - Session reaching max players.
    - All players marking themselves as ready through player properties.
    - Etc.
- Added `Network` property on `ISession` (`IClientSessionNetwork`) & `IHostSession` (`IHostSessionNetwork`) to provide control over the network managed by the Session. This allows entering and exiting games within the same multiplayer session.
- Added parameter validation for `IMultiplayerServerService.StartMultiplaySessionManagerAsync`. An `ArgumentNullException` will be thrown if the following values are not provided:
  - `MultiplaySessionManagerOptions`.
  - `MultiplaySessionManagerOptions.SessionOptions`.
  - `MultiplaySessionManagerOptions.MultiplayServerOptions`.
- Added a new `MatchmakerQueueAsset` class created when importing `.mmq` files, which can be referenced from runtime assets.
- Added a new `BuildProfile` reference and `BuildOptions` settings to the `.gsh` file description to allow Multiplay Server Hosting build deployment to perform more specific builds.
- Added an Inspector for `.gsh` files to allow editing the most common properties of the Multiplay configuration in the editor.

### Changed
- Attempting to use an unsupported connection protocol on WebGL will throw an `ArgumentException` instead of logging a warning message.
- Matchmaker queue and environment configuration files will display their status against the remote configuration in the cloud project from the Deployment Window.
- Changed `IMultiplaySessionManager.StartMultiplaySessionManagerAsync` to await the full allocation and session initialization flow and cover all potential errors.
- Removed exception thrown when trying to access the session property in `IMultiplaySessionManager.Session`, as full initialization is now guaranteed.

### Fixed
- Resolved an issue where a host could not rejoin the same session after leaving while using Distributed Authority.
- Resolved an issue where a rule without a reference could not be deployed.

## [1.1.6] - 2025-07-28

### Fixed
- Resolved an issue where a host could not rejoin the same session after leaving while using Distributed Authority.

## [1.1.5] - 2025-07-14

### Fixed
- Fixed an internal task scheduling issue that caused Session Create and Join operations to hang indefinitely on WebGL.

## [1.1.4] - 2025-06-18

### Added
- Added access to session properties on query results.
- Added host ID to session network.
- Added better error message for a GUID collision edge case for Multiplay hosted sessions.

### Fixed
- Fixed first-time Matchmaker queue file deployment that would leave the Matchmaker disabled if no Matchmaker Environment file was deployed along with the queue files.
- Fixed the potential for an awaited successful session creation to be interrupted by an exception thrown from a registered event handler.
- Fixed the network connection attempting to parse an IP address as IPV6 on platforms where IPV6 is not supported.

## [1.2.0-exp.4] - 2025-04-10

### Changed
- Changed soft dependency to the Services Deployment API in the QuickStart from `com.unity.deployment.api@1.1.0` to `com.unity.deployment@1.4.1`.

### Fixed
- Creating a new Matchmaker Queue from the QuickStart menu no longer logs a path warning issue.
- Fixed first-time Matchmaker queue file deployment that would leave the Matchmaker disabled if no Matchmaker Environment file was deployed along with the queue files.

## [1.1.3] - 2025-04-02

### Added
- Added session refresh after subscription to events.

### Fixed
- Fixed DGS not properly deleting the lobby when it leaves it or stops.
- Fixed inability to catch exceptions in the Matchmaker process if thrown after the match was found.
- Fixed lobby heartbeat start on host changed.
- Fixed `ArgumentNullException` being thrown when selecting specific `Deployment` files in the Project Window.
- Fixed inconsistent session changed events.

## [1.2.0-exp.3] - 2025-03-21

### Added
- Added Matchmaker Onboarding Section and presets for Multiplayer Center.

## [1.1.2] - 2025-03-03

### Fixed
- Calling `SubscribeToLobbyEventsAsync` multiple times on the same lobby no longer throws an exception.

## [1.1.1] - 2025-01-31

### Added
- Added migration path validation to warn users when they are using incompatible packages, namely the lobby, matchmaker, multiplay, and relay standalone SDK.

### Changed
- Increased Lobby max player count to 150.
- Removing a player not in the lobby no longer throws a `LobbyServiceException`. Instead, if verbose logging is enabled, it logs that the player was not found.
- Updated error when trying to access `MultiplayService.Instance` while in the editor.
- Subscribing to lobby events when a player is already subscribed no longer throws a `LobbyServiceException`. Instead, if verbose logging is enabled, it logs that the player is already subscribed.

### Fixed
- Fixed lobby to prevent throwing `ArgumentException: An item with the same key has already been added`.
- Fixed unauthorized error when trying to remove other players from a lobby, with service account authentication.

## [1.2.0-exp.2] - 2025-01-22

### Added
- Increased Lobby max player count to 150.
- Added migration data methods to `LobbyHandler` and `IHostSession`.

## [1.2.0-exp.1] - 2024-11-27

### Added
- Added Lobby migration data methods.

## [1.1.0] - 2024-11-19

### Added
- Added more detail in `SessionException` message for `MatchmakerAssignmentFailed` and `MatchmakerAssignmentTimeout`, and exposed the Error property via `ToString()`.
- Added two new events under `ISession`: `ISession.PlayerLeaving` and `ISession.PlayerHasLeft`.

### Changed
- Marked the `ISession.PlayerLeft` event as obsolete. It is being replaced by the new `ISession.PlayerLeaving` event.
- Increased timeout when uploading files from a build.

### Fixed
- Fixed Session backfilling configuration:
  - Corrected `WithBackfillingConfiguration` setting `backfillingLoopInterval` as the `playerConnectionTimeout`.
  - Added the missing `playerConnectionTimeout` parameter to `WithBackfillingConfiguration`.
- Deprecated the `WithBackfillingConfiguration` method and replaced it with the corrected method with the same name and the missing `playerConnectionTimeout` parameter.
- Fixed Lobby Vivox interoperability issues around joining certain channel types or joining channels that didn't match a Lobby ID when trying only to use the Vivox SDK while the Lobby SDK was present in the project.
- Fixed the Lobby Vivox channel validation to allow for positional 3D channels.
- Fixed the Server Query Protocol (SQP) responses from Multiplay Hosting servers to include correct Version and Port.
- Fixed potential issue when querying for fleet status in the Deployment Window.
- Fixed Help URL links.

## [1.0.2] - 2024-10-28

### Fixed
- Fixed WebGL support for Distributed Authority.

## [1.0.1] - 2024-10-21

### Fixed
- Fixed an issue preventing Multiplay config files from proper reimport and deployment.

## [1.0.0] - 2024-09-18

### Added
- Added QoS region selection for Distributed Authority session creation if none is passed.
- Added the ability to query the session the player has joined with `IMultiplayerService.GetJoinedSessionIdsAsync`.
- Added the ability to reconnect to a session with `IMultiplayerService.ReconnectToSessionAsync`.
- Added the ability to exclude paths on a Game Server Hosting build that supports basic patterns (*, ?).
- Added validation when accessing the `IMultiplaySessionManager.Session`.
- Added `$schema` doc field to both Queue and Environment config files.
- Added documentation on `defaultQoSRegionName`.
- Added settings to game server hosting configuration schema:
  - Added server density settings (`usageSettings`) in `fleets`.

### Changed
- Updated `com.unity.services.wire` from 1.2.6 to 1.2.7 to fix reconnection issues notably with the lobby.
- Updated matchmaker deployment window:
  - Made `defaultQoSRegionName` a valid region: `North America`.
  - Ensured `backfillEnabled` is no longer ignored.
- Made the QoS Calculator class internal.
- Marked server hardware settings as deprecated in `buildConfigurations` in Game Server Hosting configuration schema.
- Updated documentation to replace Game Server Hosting with Multiplay Hosting.
- Updated minimum required version for Netcode for GameObjects from 2.0.0-pre.3 to 2.0.0.
- Updated minimum required version for Netcode for Entities from 1.3.0-pre.2 to 1.3.2.
- Changed connection metadata visibility to only be visible to members of the session.
- Updated Distributed Authority session properties.
- Enhanced exception messages on `ClientServerBootstrap` worlds checks.

### Fixed
- Fixed matchmaker deployment window:
  - Fixed deploying queue when the remote queue has filtered pools.
  - Fixed deploying queue when the remote queue has no pools.
- Fixed default value for session property constructor.
- Fixed `SessionHandler` dropping property's index when updating them.
- Fixed session cleanup when a player polls for session updates and is kicked from the session.
- Fixed session error on deleting a non-existing session.
- Fixed port randomization compatibility with Network for GameObjects.
- Fixed occasional failure to fetch matchmaking results from P2P matches:
  - Properly uploaded these results.
- Fixed matchmaking results 204 exception.
- Fixed broken links in Multiplay Hosting documentation.
- Fixed error relating to `ENABLE_UCS_SERVER` scripting define to support limited server functionality via Play Mode using a non-server build profile.
- Fixed `TaskCanceledException` when starting an SQP server in Game Server Hosting.
- Fixed `SavePropertiesAsync` not saving session fields if properties are unchanged.
- Fixed typo in `SessionError`.

## [1.0.0-pre.1] - 2024-07-18

### Added
- Added ability to update the session published port with `NetworkConfiguration.UpdatePublishPort` to enable auto-port selection in network handlers.
- Added **View in Deployment Window** button for Game Server Hosting and Matchmaker config-as-code resource files, dependent on Deployment package version 1.4.0.

### Changed
- Updated default values for direct network options:
  - `listenIp` and `publishIp` default to `127.0.0.1`.
  - `port` defaults to `0`.
- Updated network support in sessions for Netcode for Entities to version 1.3.0-pre.2.
- Updated network support in sessions for Netcode for GameObjects v2 to version 2.0.0-pre.1 (required for Distributed Authority).

### Fixed
- Fixed issue where Game Server Hosting deploy upload may fail in some cases.

## [0.6.0] - 2024-07-10

### Added
- Added Apple privacy manifest.
- Added missing List and Delete APIs for Build configuration and Builds.
- Added missing documentation.

### Changed
- Renamed session connection operations to network branding.
- Updated `com.unity.services.wire` dependency to 1.2.6.

### Fixed
- Fixed issue where the notification system would fail to reconnect silently.

## [0.5.0] - 2024-06-18

### Added
- Added session matchmaking support for peer-to-peer and dedicated game servers.
- Added Multiplay server lifecycle support & server session management.
- Added matchmaker backfilling support for server sessions.
- Added session authorization flow for distributed authority.
- Added session filters for session matchmaking and queries.
- Added automatic attempt to leave a session when leaving the application/play mode.
- Added session viewer editor window for better observability.
- Added matchmaker deployment support.

### Changed
- Made minor improvements to sessions.

## [0.4.2] - 2024-05-28

### Changed
- Updated documentation.

## [0.4.1] - 2024-05-17

### Changed
- Updated some name changes in Netcode for GameObjects v2.0.0-exp.3.

## [0.4.0] - 2024-04-23

### Changed
- Renamed package from Multiplayer Services SDK to Multiplayer Services.

## [0.3.0] - 2024-04-04

### Added
- Added support for Distributed Authority with Netcode for GameObjects 2.0.

### Changed
- Ensured deployment window integration compatibility with Multiplay package:
  - Multiplay owns the integration from [1-1.2).
  - Unified package owns it onwards.

## [0.2.0] - 2024-03-26

### Added
- Added session delete API.

### Changed
- Set player properties on join.
- Abstracted session host concept.
- Refactored `SessionInfo`.

### Removed
- Removed `PlayerProfile` from `ISession`.

### Fixed
- Fixed session to honor session data when creating lobby.

## [0.1.0] - 2024-03-11

### Added
- Initial Multiplayer SDK sessions implementation.
- Added common Multiplayer Backend behind a feature flag:
  - Standalone functions available and support for the matchmaking flow (matchmake into a CMB session).
- Added IP Address as an optional field in Multiplay ServerConfig.

### Removed
- Removed `PostBuildHook` and `EventConnectionStateChanged`.

## [0.0.7] - 2023-08-23

### Changed
- Updated documentation.

## [0.0.6] - 2023-08-21

### Changed
- Updated README.

## [0.0.5] - 2023-08-16

### Changed
- Updated the minimum supported Editor version to 2021.3.
- Updated README with links to consolidated SDK documentation.

## [0.0.4] - 2023-08-15

### Changed
- Updated `.npmignore`.

### Removed
- Removed samples from the package.

## [0.0.3] - 2023-08-14

### Changed
- Unexported `MatchHandlerImpl`.
- Made API changes.

## [0.0.2] - 2023-08-10

### Changed
- Updated README.

## [0.0.1] - 2023-08-09

### Added
- Initial SDK.
