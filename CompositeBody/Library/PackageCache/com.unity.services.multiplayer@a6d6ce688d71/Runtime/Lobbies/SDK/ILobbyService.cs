using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.DistributedAuthority.Models;
using Unity.Services.Lobbies.Models;

namespace Unity.Services.Lobbies
{
    /// <summary>
    /// Service for Lobbies.
    /// Provides user the ability to create, delete, update, and query Lobbies.
    /// Includes operations for interacting with given players in a Lobby context.
    /// </summary>
    public interface ILobbyService
    {
        /// <summary>
        /// Applies concurrency control by adding an <a
        /// href="https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/If-Match">If-match
        /// header</a> with the latest version of the lobby to applicable lobby operations.
        /// <br/>
        /// When enabled, operations are only allowed if the latest
        /// version matches the current version on the server.
        /// <br/>
        /// Applies to the following operations:
        /// <br/>
        /// • <see cref="DeleteLobbyAsync">Deleting a lobby</see>
        /// <br/>
        /// • <see cref="RemovePlayerAsync">Removing a player from a lobby</see>
        /// <br/>
        /// • <see cref="UpdateLobbyAsync">Updating a lobby information</see>
        /// <br/>
        /// • <see cref="UpdatePlayerAsync">Updating a lobby player</see>
        /// </summary>
        public bool ConcurrencyControlEnabled { get; set; }

        /// <summary>
        /// Create a Lobby with a given name and specified player limit.
        /// Async operation.
        /// </summary>
        /// <param name="lobbyName">Name of new lobby.</param>
        /// <param name="maxPlayers">Player limit.</param>
        /// <param name="options">Optional request parameters.</param>
        /// <returns>Lobby data for the lobby that was just created.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="lobbyName"/> is <c>null</c> or
        /// only contains whitespaces.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown when <paramref name="maxPlayers"/> is less than
        /// one.</exception>
        /// <exception cref="Unity.Services.Lobbies.LobbyServiceException">Thrown when the lobby service returns an
        /// error.</exception>
        Task<Models.Lobby> CreateLobbyAsync(string lobbyName, int maxPlayers, CreateLobbyOptions options = default);

        /// <summary>
        /// Create or join a Lobby with a given name and ID and specified player limit.
        /// Async operation.
        /// </summary>
        /// <param name="lobbyId">ID of the lobby to create/join.</param>
        /// <param name="lobbyName">Name of the lobby to create/join.</param>
        /// <param name="maxPlayers">Player limit.</param>
        /// <param name="options">Optional request parameters.</param>
        /// <returns>Lobby data for the lobby that was just created/joined.</returns>
        /// <exception cref="System.ArgumentNullException">Throw when <paramref name="lobbyId"/> or <paramref
        /// name="lobbyName"/> is empty or only contains whitespaces.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown when <paramref name="maxPlayers"/> is less than
        /// one.</exception>
        /// <exception cref="Unity.Services.Lobbies.LobbyServiceException">Thrown when the lobby service returns an
        /// error.</exception>
        Task<Models.Lobby> CreateOrJoinLobbyAsync(string lobbyId, string lobbyName, int maxPlayers, CreateLobbyOptions options = default);

        /// <summary>
        /// A subscription to the given lobby is created and the given callbacks are associated with it.
        /// The return ILobbyEvents interface can be used to unsubscribe and re-subscribe to the connection.
        /// The callbacks object provided will be used to provide the notifications from the subscription.
        /// </summary>
        /// <param name="lobbyId">The ID of the lobby you are subscribing to events for.</param>
        /// <param name="callbacks">The callbacks you provide, which will be called as notifications arrive from the subscription.</param>
        /// <returns>An interface to change the callbacks associated with the subscription, or to unsubscribe and re-subscribe to the lobby's events.</returns>
        Task<ILobbyEvents> SubscribeToLobbyEventsAsync(string lobbyId, LobbyEventCallbacks callbacks);

        /// <summary>
        /// Deletes a lobby specified by its ID.
        /// </summary>
        /// <param name="lobbyId">
        /// The ID of the lobby to delete. Cannot
        /// be <c>null</c>, empty, or whitespace.
        /// </param>
        /// <returns>
        /// A task representing the asynchronous delete operation.
        /// </returns>
        /// <exception cref="System.ArgumentNullException">
        /// Thrown when <paramref name="lobbyId"/> is
        /// <c>null</c>, empty, or contains only whitespace.
        /// </exception>
        /// <exception cref="Unity.Services.Lobbies.LobbyServiceException">
        /// Thrown when the lobby service returns an error.
        /// </exception>
        /// <remarks>
        /// If <see cref="ConcurrencyControlEnabled"/> is enabled, an
        /// If-Match header with the latest lobby version will be sent and
        /// the deletion will only succeed if the server version matches.
        /// </remarks>
        /// <seealso cref="ConcurrencyControlEnabled"/>
        Task DeleteLobbyAsync(string lobbyId);

        /// <summary>
        /// Async Operation.
        /// Get currently joined lobbies.
        /// </summary>
        /// <returns>List of lobbies the active player has joined.</returns>
        /// <exception cref="Unity.Services.Lobbies.LobbyServiceException">Thrown when the lobby service returns an
        /// error.</exception>
        Task<List<string>> GetJoinedLobbiesAsync();

        /// <summary>
        /// Retrieve data for a Lobby by specifying a Lobby ID.
        /// Async operation.
        /// </summary>
        /// <param name="lobbyId">ID of the Lobby to retrieve.</param>
        /// <returns>Lobby data.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="lobbyId"/> is <c>null</c> or only
        /// contains whitespaces.</exception>
        /// <exception cref="Unity.Services.Lobbies.LobbyServiceException">Thrown when the lobby service returns an
        /// error.</exception>
        /// <seealso href="https://services.docs.unity.com/lobby/v1/#tag/Lobby/operation/getLobby"/>
        Task<Models.Lobby> GetLobbyAsync(string lobbyId);

        /// <summary>
        /// Retrieve data for a Lobby by specifying a Lobby ID.
        /// Async operation.
        /// </summary>
        /// <param name="lobbyId">ID of the Lobby to retrieve.</param>
        /// <param name="ifNoneMatchVersion">If provided, the version will be submitted in the If-None-Match header.</param>
        /// <returns>Lobby data.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="lobbyId"/> is <c>null</c> or only
        /// contains whitespaces.</exception>
        /// <exception cref="Unity.Services.Lobbies.LobbyServiceException">Thrown when the lobby service returns an
        /// error.</exception>
        /// <seealso href="https://services.docs.unity.com/lobby/v1/#tag/Lobby/operation/getLobby"/>
        Task<Models.Lobby> GetLobbyAsync(string lobbyId, string ifNoneMatchVersion);

        /// <summary>
        /// Send a heartbeat ping to keep the Lobby active.
        /// Async operation.
        /// </summary>
        /// <param name="lobbyId">ID of the Lobby to ping.</param>
        /// <returns>Awaitable task.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="lobbyId"/> is <c>null</c> or only
        /// contains whitespaces.</exception>
        /// <exception cref="Unity.Services.Lobbies.LobbyServiceException">Thrown when the lobby service returns an
        /// error.</exception>
        Task SendHeartbeatPingAsync(string lobbyId);

        /// <summary>
        /// Join a Lobby using a given Lobby Invite Code.
        /// Async operation.
        /// </summary>
        /// <param name="lobbyCode">Invite Code for target lobby.</param>
        /// <param name="options">Optional request parameters.</param>
        /// <returns>Lobby data for the lobby joined.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="lobbyCode"/> is <c>null</c> or
        /// only contains whitespaces.</exception>
        /// <exception cref="Unity.Services.Lobbies.LobbyServiceException">Thrown when the lobby service returns an
        /// error.</exception>
        Task<Models.Lobby> JoinLobbyByCodeAsync(string lobbyCode, JoinLobbyByCodeOptions options = default);

        /// <summary>
        /// Join a Lobby by specifying the Lobby ID.
        /// Async operation.
        /// </summary>
        /// <param name="lobbyId">ID of the Lobby to join.</param>
        /// <param name="options">Optional request parameters.</param>
        /// <returns>Lobby data for the lobby joined.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="lobbyId"/> is <c>null</c> or only
        /// contains whitespaces.</exception>
        /// <exception cref="Unity.Services.Lobbies.LobbyServiceException">Thrown when the lobby service returns an
        /// error.</exception>
        Task<Models.Lobby> JoinLobbyByIdAsync(string lobbyId, JoinLobbyByIdOptions options = default);

        /// <summary>
        /// Query and retrieve a list of lobbies that meet specified query parameters.
        /// Async operation.
        /// </summary>
        /// <param name="options">Query parameters.</param>
        /// <returns>Query response that includes list of Lobbies meeting specified parameters.</returns>
        /// <exception cref="Unity.Services.Lobbies.LobbyServiceException">Thrown when the lobby service returns an
        /// error.</exception>
        Task<QueryResponse> QueryLobbiesAsync(QueryLobbiesOptions options = default);

        /// <summary>
        /// Query available lobbies and join a randomly selected instance.
        /// Async operation.
        /// </summary>
        /// <param name="options">Optional parameters (includes queryable arguments).</param>
        /// <returns>Lobby data for the lobby joined.</returns>
        /// <exception cref="Unity.Services.Lobbies.LobbyServiceException">Thrown when the lobby service returns an
        /// error.</exception>
        Task<Models.Lobby> QuickJoinLobbyAsync(QuickJoinLobbyOptions options = default);

        /// <summary>
        /// Removes a player from the specified lobby.
        /// </summary>
        /// <param name="lobbyId">
        /// The target lobby's ID. Cannot be <c>null</c>,
        /// empty, or consist only of whitespace.
        /// </param>
        /// <param name="playerId">
        /// The player ID to remove. Cannot be <c>null</c>,
        /// empty, or consist only of whitespace.
        /// </param>
        /// <returns>
        /// A task representing the asynchronous remove operation.
        /// </returns>
        /// <exception cref="System.ArgumentNullException">
        /// Thrown when <paramref name="lobbyId"/> or
        /// <paramref name="playerId"/> is <c>null</c>,
        /// empty, or consists only of whitespace.
        /// </exception>
        /// <exception cref="Unity.Services.Lobbies.LobbyServiceException">
        /// Thrown when the lobby service returns an error.
        /// </exception>
        /// <remarks>
        /// If <see cref="ConcurrencyControlEnabled"/> is enabled, an
        /// If-Match header with the latest lobby version will be sent and
        /// the removal will only succeed if the server version matches.
        /// </remarks>
        /// <seealso cref="ConcurrencyControlEnabled"/>
        Task RemovePlayerAsync(string lobbyId, string playerId);

        /// <summary>
        /// Updates the specified lobby's properties using the provided options.
        /// </summary>
        /// <param name="lobbyId">
        /// The ID of the lobby to update. Cannot
        /// be <c>null</c>, empty, or whitespace.
        /// </param>
        /// <param name="options">
        /// Options describing which lobby fields
        /// to update. Cannot be <c>null</c>.
        /// </param>
        /// <returns>
        /// A task that resolves to the updated <see cref="Models.Lobby"/>.
        /// </returns>
        /// <exception cref="System.ArgumentNullException">
        /// Thrown when <paramref name="lobbyId"/> is <c>null</c>, empty, or
        /// whitespace, or when <paramref name="options"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="Unity.Services.Lobbies.LobbyServiceException">
        /// Thrown when the lobby service returns an error.
        /// </exception>
        /// <remarks>
        /// When <see cref="ConcurrencyControlEnabled"/> is enabled, an If-Match
        /// header with the latest lobby version will be sent. The update will
        /// only succeed if the server version matches the provided version.
        /// </remarks>
        /// <seealso cref="ConcurrencyControlEnabled"/>
        Task<Models.Lobby> UpdateLobbyAsync(string lobbyId, UpdateLobbyOptions options);

        /// <summary>
        /// Updates the specified player's lobby-associated
        /// data using the provided options.
        /// </summary>
        /// <param name="lobbyId">
        /// The ID of the lobby that contains the player.
        /// Cannot be <c>null</c>, empty, or whitespace.
        /// </param>
        /// <param name="playerId">
        /// The ID of the player to update. Cannot
        /// be <c>null</c>, empty, or whitespace.
        /// </param>
        /// <param name="options">
        /// Options describing which player fields
        /// to update. Cannot be <c>null</c>.
        /// </param>
        /// <returns>
        /// A task that resolves to the updated <see cref="Models.Lobby"/>.
        /// </returns>
        /// <exception cref="System.ArgumentNullException">
        /// Thrown when <paramref name="lobbyId"/>, <paramref name="playerId"/>,
        /// or <paramref name="options"/> is <c>null</c>, empty, or whitespace.
        /// </exception>
        /// <exception cref="Unity.Services.Lobbies.LobbyServiceException">
        /// Thrown when the lobby service returns an error.
        /// </exception>
        /// <remarks>
        /// When <see cref="ConcurrencyControlEnabled"/> is enabled, an If-Match
        /// header with the latest lobby version will be sent. The update will
        /// only succeed if the server version matches the provided version.
        /// <br/>
        /// For the modern, streamlined workflow see <a
        /// href="https://docs.unity.com/ugs/en-us/manual/mps-sdk/manual">here</a>.
        /// </remarks>
        /// <seealso cref="ConcurrencyControlEnabled"/>
        Task<Models.Lobby> UpdatePlayerAsync(string lobbyId, string playerId, UpdatePlayerOptions options);

        /// <summary>
        /// Reconnects to the lobby.
        /// </summary>
        /// <param name="lobbyId">The ID of the lobby to reconnect to.</param>
        /// <returns>The lobby you reconnected to.</returns>
        Task<Models.Lobby> ReconnectToLobbyAsync(string lobbyId);

        /// <summary>
        /// Get lobby migration data information.
        /// </summary>
        /// <param name="lobbyId">The ID of the lobby to get migration data information.</param>
        /// <returns>The migration data information for the lobby.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="lobbyId"/> is <c>null</c> or only
        /// contains whitespaces.</exception>
        /// <exception cref="Unity.Services.Lobbies.LobbyServiceException">Thrown when the lobby service returns an
        /// error.</exception>
        Task<Models.MigrationDataInfo> GetMigrationDataInfoAsync(string lobbyId);

        /// <summary>
        /// Download lobby migration data.
        /// </summary>
        /// <param name="migrationDataInfo">Is the lobby migration data information.</param>
        /// <param name="options">Is the migration Data download request Parameters.</param>
        /// <returns> Task for <see cref="MigrationData"/>. <see cref="MigrationData.Data"/> is null if not present.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="migrationDataInfo"/> is <c>null</c> or
        /// does not contain a valid Read url.</exception>
        /// <exception cref="Unity.Services.Lobbies.LobbyServiceException">Thrown when the lobby service returns an
        /// error.
        /// <see cref="Unity.Services.Lobbies.LobbyExceptionReason.MigrationDataRequestTimeout"/> in case of request timeout.</exception>
        /// <remarks>
        /// This task is not cancellable. Timeout can be configured in DownloadMigrationDataOptions.
        /// </remarks>
        Task<LobbyMigrationData> DownloadMigrationDataAsync(MigrationDataInfo migrationDataInfo, LobbyDownloadMigrationDataOptions options);

        /// <summary>
        /// Upload lobby migration data.
        /// </summary>
        /// <param name="migrationDataInfo">Is the lobby migration data information.</param>
        /// <param name="data">Is the binary migration data.</param>
        /// <param name="options">Is the migration data download request Parameters.</param>
        /// <returns> Task for <see cref="LobbyUploadMigrationDataResults"/>.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="migrationDataInfo"/> is <c>null</c> or
        /// does not contain a valid Write url or when <paramref name="data"/> is <c>null</c> or empty.</exception>
        /// <exception cref="Unity.Services.Lobbies.LobbyServiceException">Thrown when the lobby service returns an
        /// error.The <see cref="Unity.Services.Lobbies.LobbyExceptionReason"/> property reflects the error.
        /// <see cref="Unity.Services.Lobbies.LobbyExceptionReason.MigrationDataRequestTimeout"/> in case of request timeout.</exception>
        /// <remarks>
        /// This task is not cancellable. Timeout can be configured in DownloadMigrationDataOptions.
        /// </remarks>
        Task<LobbyUploadMigrationDataResults> UploadMigrationDataAsync(MigrationDataInfo migrationDataInfo, byte[] data, LobbyUploadMigrationDataOptions options);
    }

    /// <summary>
    /// This interface is marked for deprecation. Please use ILobbyService instead.
    /// </summary>
    public interface ILobbyServiceSDK : ILobbyService
    {
    }
}
