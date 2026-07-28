using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication.Internal;
using Unity.Services.Core;
using Unity.Services.Wire.Internal;

using Logger = Unity.Services.Multiplayer.Logger;

namespace Unity.Services.Lobbies.Internal
{
    internal class LobbyChannel : ILobbyEvents
    {
        private readonly IChannel channelSubscription;

        private int mostRecentEventVersion;
        private readonly IPlayerId playerIdComponent;
        /// <summary>
        /// The Lobby id associated to this channel
        /// </summary>
        private readonly string lobbyId;
        /// <summary>
        ///  The lobbyService used to retrieve cache and Get Lobby
        /// </summary>
        private readonly ILobbyService lobbyService;
        /// <summary>
        /// Event queue to avoid collision between events reception and Get Lobby calls
        /// </summary>
        private readonly SortedList<int, ILobbyChanges> eventProcessQueue;
        private readonly object eventLock = new object();
        private readonly object mostRecentEventVersionLock = new object();

        public LobbyEventCallbacks Callbacks { get; }

        internal LobbyChannel(IPlayerId playerId, IChannel channel, LobbyEventCallbacks callbacks, string lobbyId, ILobbyService lobbyService)
        {
            playerIdComponent = playerId;
            channelSubscription = channel;
            Callbacks = callbacks;
            eventProcessQueue = new SortedList<int, ILobbyChanges>();
            this.lobbyId = lobbyId;
            this.lobbyService = lobbyService;
            channelSubscription.MessageReceived += async(payload) => { await OnLobbySubscriptionMessage(payload, callbacks); };
            channelSubscription.KickReceived += () => { OnLobbySubscriptionKick(callbacks); };
            channelSubscription.NewStateReceived += (state) => { OnLobbySubscriptionNewState(state, callbacks); };
        }

        public async Task SubscribeAsync()
        {
            try
            {
                await channelSubscription.SubscribeAsync();
            }
            catch (RequestFailedException ex)
            {
                switch (ex.ErrorCode)
                {
                    case 23002: /* WireErrorCode.CommandFailed */
                    case 23003: /* WireErrorCode.ConnectionFailed */
                        throw new LobbyServiceException(LobbyExceptionReason.SubscriptionToLobbyLostWhileBusy, $"The connection was lost or dropped while attempting to subscribe.", ex);
                    case 23008: /* WireErrorCode.AlreadySubscribed */
                        Logger.LogVerbose("You are already subscribed to this lobby, you do not need to subscribe again.");
                        break;
                    default: throw new LobbyServiceException(LobbyExceptionReason.LobbyEventServiceConnectionError, $"There was an error when trying to connect to the lobby service for events. Ensure a valid Lobby ID was sent. Error Code[{ex.ErrorCode}].", ex);
                }
            }
        }

        public async Task UnsubscribeAsync()
        {
            try
            {
                await channelSubscription.UnsubscribeAsync();
            }
            catch (RequestFailedException ex)
            {
                switch (ex.ErrorCode)
                {
                    case 23002: /* WireErrorCode.CommandFailed */ throw new LobbyServiceException(LobbyExceptionReason.SubscriptionToLobbyLostWhileBusy, $"The connection was lost or dropped while attempting to unsubscribe.", ex);
                    case 23009: /* WireErrorCode.AlreadyUnsubscribed */ throw new LobbyServiceException(LobbyExceptionReason.AlreadyUnsubscribedFromLobby, $"You are already unsubscribed from this lobby, you do not need to unsubscribe again.", ex);
                    default: throw new LobbyServiceException(LobbyExceptionReason.LobbyEventServiceConnectionError, $"There was an error when trying to connect to the lobby service for events. Ensure a valid Lobby ID was sent. Error Code[{ex.ErrorCode}].", ex);
                }
            }
        }

        private async Task OnLobbySubscriptionMessage(string payload, LobbyEventCallbacks callbacks)
        {
            try
            {
                var changes = LobbyPatcher.GetLobbyChanges(payload);

                if (mostRecentEventVersion < changes.Version.Value)
                {
                    lock (mostRecentEventVersionLock)
                    {
                        mostRecentEventVersion = changes.Version.Value;
                    }
                }

                await HandleLobbyChanges(changes, callbacks);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
        }

        private void OnLobbySubscriptionKick(LobbyEventCallbacks callbacks)
        {
            callbacks.InvokeKickedFromLobby();
        }

        private void OnLobbySubscriptionNewState(SubscriptionState state, LobbyEventCallbacks callbacks)
        {
            switch (state)
            {
                case SubscriptionState.Unsubscribed:
                {
                    (lobbyService as ILobbyServiceInternal)!.GetLobbyCacher().ResetLobby(lobbyId);
                    callbacks.InvokeLobbyEventConnectionStateChanged(LobbyEventConnectionState.Unsubscribed);
                    break;
                }
                case SubscriptionState.Subscribing: callbacks.InvokeLobbyEventConnectionStateChanged(LobbyEventConnectionState.Subscribing); break;
                case SubscriptionState.Synced: callbacks.InvokeLobbyEventConnectionStateChanged(LobbyEventConnectionState.Subscribed); break;
                case SubscriptionState.Unsynced: callbacks.InvokeLobbyEventConnectionStateChanged(LobbyEventConnectionState.Unsynced); break;
                case SubscriptionState.Error: callbacks.InvokeLobbyEventConnectionStateChanged(LobbyEventConnectionState.Error); break;
                default: callbacks.InvokeLobbyEventConnectionStateChanged(LobbyEventConnectionState.Unknown); break;
            }
        }

        private async Task HandleLobbyChanges(ILobbyChanges changes, LobbyEventCallbacks callbacks)
        {
            // Enqueue event for processing and re-order the queue
            lock (eventLock)
            {
                eventProcessQueue.Add(changes.Version.Value, changes);

                // Return if an event is already in process
                if (eventProcessQueue.Count > 1)
                {
                    return;
                }
            }

            // Resync the count
            var eventCount = 0;
            lock (eventLock)
            {
                eventCount = eventProcessQueue.Count;
            }

            // Get the next event in line but
            // Don't remove it before it's done processing
            while (eventCount != 0)
            {
                // Get the event that has the lowest version
                ILobbyChanges nextToProcess;
                lock (eventLock)
                {
                    nextToProcess = eventProcessQueue.Values[0];
                }

                try
                {
                    await ProcessEvent(nextToProcess, callbacks);
                }
                catch (Exception e)
                {
                    Logger.LogException(e);
                }
                finally
                {
                    // Finally remove the event from the process queue and check if there are new ones
                    lock (eventLock)
                    {
                        eventProcessQueue.Remove(nextToProcess.Version.Value);
                        eventCount = eventProcessQueue.Count;
                    }
                }
            }
        }

        private async Task ProcessEvent(ILobbyChanges nextToProcess, LobbyEventCallbacks callbacks)
        {
            var eventLobbyVersion = nextToProcess.Version.Value;
            var lobbyCacher = (lobbyService as ILobbyServiceInternal) !.GetLobbyCacher();
            // If the lobby cache doesn't exist yet,
            // or the event version is not the event version we are waiting for
            if
            (
                !nextToProcess.LobbyDeleted &&
                (!lobbyCacher.TryGetLobbyCache(lobbyId, out Models.Lobby cachedLobby) ||
                 eventLobbyVersion != cachedLobby.Version + 1)
            )
            {
                // Ignore event versions lower than the current cached Lobby version
                if (cachedLobby != null && eventLobbyVersion <= cachedLobby.Version)
                {
                    return;
                }
                // Otherwise get the lobby ensure the cache is up to date.
                await lobbyService.GetLobbyAsync(lobbyId);
                lobbyCacher.TryGetLobbyCache(lobbyId, out cachedLobby);
            }

            // The cached lobby is now up to date, we can process the event
            lobbyCacher.UpdateLobbyCache(lobbyId, nextToProcess);
        }

        private bool ResolveTrivialEvent(ILobbyChanges changes, LobbyEventCallbacks callbacks, Models.Lobby cachedLobby)
        {
            // If there is no cache, we need to execute a Get Lobby
            if (cachedLobby == null)
                return false;

            var eventLobbyVersion = changes.Version.Value;

            // Ignore event version lower than the current cached Lobby version
            if (eventLobbyVersion <= cachedLobby.Version)
            {
                return true;
            }

            // Execute and apply event if it's the version we are waiting for
            if (eventLobbyVersion == cachedLobby.Version + 1)
            {
                if (!WasRemovedFromLobby(changes, cachedLobby))
                    changes.ApplyToLobby(cachedLobby);
                callbacks.InvokeLobbyChanged(changes);
                return true;
            }

            return false;
        }

        private bool WasRemovedFromLobby(ILobbyChanges changes, Models.Lobby cachedLobby)
        {
            var lobbyCacher = (lobbyService as ILobbyServiceInternal) !.GetLobbyCacher();

            if (cachedLobby == null || !changes.PlayerLeft.Changed)
                return false;

            foreach (var playerIdx in changes.PlayerLeft.Value)
            {
                if (cachedLobby.Players[playerIdx].Id.Equals(playerIdComponent.PlayerId))
                {
                    lobbyCacher.RemoveLobbyCache(lobbyId);
                    return true;
                }
            }

            return false;
        }
    }
}
