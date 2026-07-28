using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Core.Scheduler.Internal;
using Unity.Services.Matchmaker;
using Unity.Services.Matchmaker.Models;

namespace Unity.Services.Multiplayer
{
    class MatchmakerModule : IModule
    {
        public StoredMatchmakingResults MatchmakingResults { get; private set; }

        readonly ISession m_Session;

        // Module is enabled if the session is the result of matchmaking
        bool m_Enabled;

        // If the module is enabled and was initialized
        bool m_Initialized;

        // start backfilling on initialization
        bool m_StartBackfillOnInit;

        // determine if players are automatically removed from the backfill
        bool m_AutomaticallyRemovePlayersFromBackfill = true;

        // Determine if backfilling should start automatically when a player is missing
        bool m_AutomaticallyStartBackfillingWhenPlayerIsMissing = true;

        // determine if backfill is active
        bool m_BackfillIsActive;

        // Determine the timeout before a player is automatically removed from the backfill if they never connect
        // If value = 0, the player will never be removed
        int m_PlayerConnectionTimeout = 30;

        // Determine how often (in seconds) the backfill approval loop should run
        int m_BackfillingLoopInterval = 1;

        BackfillTicket m_LocalBackfillTicket;

        StoredMatchProperties m_MatchProperties;

        readonly Dictionary<string, DateTime> m_PlayerWaitingConnection = new();

        long? m_ApproveActionID;
        bool m_LocalDataDirty;
        string m_Connection;

        readonly IActionScheduler m_ActionScheduler;
        readonly IMatchmakerService m_MatchmakerService;

        const string k_TeamIdProperty = "TeamId";
        const string k_TeamNameProperty = "TeamName";

        public MatchmakerModule(
            ISession session,
            IActionScheduler actionScheduler,
            IMatchmakerService matchmakerService)
        {
            m_ActionScheduler = actionScheduler;
            m_MatchmakerService = matchmakerService;
            m_Session = session;
        }

        public async Task InitializeAsync()
        {
            Logger.LogVerbose($"Matchmaker Module InitializeAsync");

            if (!m_Enabled)
            {
                // Skipping Matchmaker Module Initialization - This session was not the result of matchmaking.
                return;
            }

            if (!await FetchMatchmakingResults())
            {
                throw new SessionException("Error while fetching Matchmaking Results", SessionError.InvalidMatchmakerResults);
            }

            if (m_Session.MaxPlayers < MatchmakingResults.MatchProperties.MaxPlayers)
            {
                throw new SessionException(
                    $"MaxPlayers in Session ({m_Session.MaxPlayers}) is less than the MaxPlayers configured in Matchmaker rules ({MatchmakingResults.MatchProperties.MaxPlayers}).",
                    SessionError.InvalidCreateSessionOptions);
            }

            m_Initialized = true;

            if (m_Session.IsServer)
            {
                InitializeBackfilling();
            }
        }

        public async Task LeaveAsync()
        {
            if (!m_Initialized)
            {
                return;
            }

            if (m_LocalBackfillTicket != null)
            {
                await StopBackfillingAsync();
            }
        }

        internal void Enable()
        {
            Logger.LogVerbose("MatchmakerModule.Enable");
            m_Enabled = true;
        }

        /// <summary>
        /// Allow to start backfilling the session.
        /// Backfilling is currently only supported on Game Server Hosting.
        /// </summary>
        public async Task StartBackfillingAsync()
        {
            Logger.LogVerbose("StartBackfillingAsync");

            if (!m_Initialized)
                throw new SessionException("This session cannot be backfilled as it was not created by matchmaking.", SessionError.InvalidOperation);

            if (m_ApproveActionID != null)
            {
                Logger.LogVerbose("Backfilling is already in progress.");
                return;
            }

            if (!ShouldBackfill())
            {
                Logger.LogVerbose("Cannot start backfilling - Session is full or locked.");
                return;
            }

            var createBackfillOptions = new CreateBackfillTicketOptions(
                queueName: MatchmakingResults.QueueName,
                connection: m_Connection,
                properties: new BackfillTicketProperties(m_MatchProperties.ToMatchProperties()),
                matchId: m_Session.Id,
                poolId: MatchmakingResults.PoolId
            ).WithCustomConnection();

            string backfillTicketId;
            try
            {
                backfillTicketId = await m_MatchmakerService.CreateBackfillTicketAsync(createBackfillOptions);
            }
            catch (Exception e)
            {
                Logger.LogError("Error while creating backfill ticket: " + e.Message);
                return;
            }

            var backfillTicket = await ApproveBackfillTicket(backfillTicketId);
            if (backfillTicket != null)
                m_LocalBackfillTicket = backfillTicket;

            Logger.LogVerbose($"Starting backfilling. ticket ID: {m_LocalBackfillTicket.Id}");
            m_BackfillIsActive = true;

            ScheduleApproveBackfillLoop();
        }

        /// <summary>
        /// Stop backfilling the session.
        /// </summary>
        public async Task StopBackfillingAsync()
        {
            if (!m_BackfillIsActive || !m_Initialized)
                return;

            m_BackfillIsActive = false;
            Logger.LogVerbose($"Stopping backfilling.");

            if (m_ApproveActionID != null)
            {
                m_ActionScheduler.CancelAction(m_ApproveActionID.Value);
                m_ApproveActionID = null;
            }

            if (m_LocalBackfillTicket != null)
            {
                try
                {
                    await m_MatchmakerService.DeleteBackfillTicketAsync(m_LocalBackfillTicket.Id);
                }
                catch (Exception e)
                {
                    Logger.LogError($"Error while deleling backfill ticket: {e.Message}");
                    throw;
                }
            }
        }

        int GetBackfillPlayerCount()
        {
            return m_LocalBackfillTicket?.Properties.MatchProperties.Players.Count ?? 0;
        }

        bool IsBackfillFull()
        {
            return GetBackfillPlayerCount() >= m_Session.MaxPlayers;
        }

        bool ShouldBackfill()
        {
            return !m_Session.IsLocked && !IsBackfillFull();
        }

        void OnPlayerJoined(string playerId)
        {
            Logger.LogVerbose($"Backfill: OnPlayerJoined: {playerId}");

            // Keep match properties up to date to be able to create a backfill ticket later on.
            if (!LocalMatchPropertiesAreValid())
            {
                throw new SessionException("State of the local match is invalid. Cannot add player.", SessionError.InvalidLocalMatchProperties);
            }

            // If player is already in the local match properties, do not add it again
            if (m_MatchProperties.Players.Exists(p => p.Id == playerId))
            {
                return;
            }

            // Try to find the player in the backfill ticket
            bool playerFoundInBackfill = false;

            if (m_LocalBackfillTicket != null)
            {
                if (!LocalBackfillTicketIsValid())
                {
                    throw new SessionException("State of the backfill ticket is invalid. Cannot add player.", SessionError.InvalidBackfillTicket);
                }

                if (FindPlayerByIdInBackfillTicket(playerId, out var player))
                {
                    Logger.LogVerbose($"Player: {playerId} joined from backfilling.");
                    Logger.LogVerbose($"Backfilling status: {GetBackfillPlayerCount()}/{m_Session.MaxPlayers}");

                    playerFoundInBackfill = true;

                    m_PlayerWaitingConnection.Remove(playerId);

                    // make sure we don't add the player twice
                    if (!m_MatchProperties.Players.Exists(p => p.Id == playerId))
                    {
                        m_MatchProperties.Players.Add(player);
                    }

                    if (FindPlayerTeamInBackfill(playerId, out var teamFromBackfill))
                    {
                        if (FindTeamByIdInLocalMatchProperties(teamFromBackfill.TeamId, out var teamInMatchProperties))
                        {
                            // Make sure the team does not alread contains the player
                            // The team of the player exists in the local match properties
                            // We add the player in the team
                            if (!teamInMatchProperties.PlayerIds.Contains(playerId))
                                teamInMatchProperties.PlayerIds.Add(playerId);
                        }
                        else
                        {
                            // The team does not exist in the local match properties
                            // We create a copy of the team from the backfill with the player and add it on the local match properties
                            var newTeam = new Team(teamFromBackfill.TeamName, teamFromBackfill.TeamId,
                                new List<string> { playerId });
                            m_MatchProperties.Teams.Add(newTeam);
                        }
                    }
                    else
                    {
                        throw new SessionException(
                            $"Could not find team with id {teamFromBackfill.TeamId} of the player in the backfill ticket.",
                            SessionError.InvalidBackfillTicket);
                    }
                }
            }

            // If we could not find the player in backfill try to update the match properties and the backfill using the properties on the player
            if (!playerFoundInBackfill)
            {
                Logger.LogVerbose($"Could not find player in backfill ticket. Trying to add the player to the backfill ticket using the session player property {k_TeamIdProperty}.");
                var sessionPlayer = m_Session.Players.FirstOrDefault(p => p.Id == playerId);
                if (sessionPlayer == null)
                {
                    // Cannot find the player on the session - assuming the player left right after joining
                    Logger.LogVerbose($"Cannot find player {playerId} in the session. Player may have left already");
                    return;
                }

                if (!sessionPlayer.Properties.TryGetValue(k_TeamIdProperty, out var teamIdProperty))
                {
                    // Throwing as this is an invalid state that could lead to issues with the matchmaker service and the backfilling process.
                    throw new SessionException(
                        $"Player does not have {k_TeamIdProperty} property. Cannot add player to the backfill ticket.", SessionError.PlayerMissingTeamProperties);
                }

                // add player to local match properties
                var teamId = teamIdProperty.Value;
                if (!FindTeamByIdInLocalMatchProperties(teamId, out var team))
                {
                    // Adding the team to the local match properties
                    team = new Team(teamId, teamId, new List<string>());
                    m_MatchProperties.Teams.Add(team);
                }

                var matchmakerPlayer = new Unity.Services.Matchmaker.Models.Player(sessionPlayer.Id, sessionPlayer.Properties);
                team.PlayerIds.Add(playerId);
                m_MatchProperties.Players.Add(matchmakerPlayer);

                // Add to backfill ticket if it exists
                if (m_LocalBackfillTicket != null)
                {
                    if (!BackfillTicketIsValid())
                    {
                        throw new SessionException("State of the backfill ticket is invalid. Cannot add player.", SessionError.InvalidBackfillTicket);
                    }

                    // Make sure player is not already in the backfill ticket
                    if (!FindPlayerByIdInBackfillTicket(matchmakerPlayer.Id, out _))
                    {
                        m_LocalBackfillTicket.Properties.MatchProperties.Players.Add(matchmakerPlayer);
                        m_LocalDataDirty = true;
                    }

                    if (FindTeamByIdInBackfillTicket(teamId, out var backfillTeam))
                    {
                        if (!backfillTeam.PlayerIds.Contains(playerId))
                        {
                            backfillTeam.PlayerIds.Add(playerId);
                            m_LocalDataDirty = true;
                        }
                    }
                    else
                    {
                        // If team does not exist in the backfill ticket, create a new team and add the player
                        // Try to get the team name from the player properties
                        sessionPlayer.Properties.TryGetValue(k_TeamIdProperty, out var teamNameProperty);
                        var teamName = teamNameProperty?.Value;
                        backfillTeam = new Team(teamName, teamId, new List<string> { playerId });
                        m_LocalBackfillTicket.Properties.MatchProperties.Teams.Add(backfillTeam);
                        m_LocalDataDirty = true;
                    }
                }
            }
        }

        void OnPlayerLeft(string playerId)
        {
            Logger.LogVerbose($"Backfill: OnPlayerLeft: {playerId}");
            // Remove player from backfill
            if (m_AutomaticallyRemovePlayersFromBackfill)
            {
                // Remove from local match properties
                if (!LocalMatchPropertiesAreValid())
                {
                    throw new SessionException("State of the local match properties are invalid. Cannot remove player.", SessionError.InvalidLocalMatchProperties);
                }

                var player = m_MatchProperties.Players.FirstOrDefault(p => p.Id == playerId);
                if (player == null)
                {
                    Logger.LogVerbose("Player not found in match properties. Cannot remove player.");
                }
                else
                {
                    if (!m_MatchProperties.Players.Remove(player))
                    {
                        Logger.LogVerbose("Failed removing the player from the local match properties.");
                    }
                    else
                    {
                        if (!FindPlayerTeamInLocalMatchProperties(playerId, out var team))
                        {
                            Logger.LogVerbose("Cannot find the team for the player. Cannot remove player from the team on the match properties.");
                        }
                        else
                        {
                            team.PlayerIds.Remove(playerId);
                        }
                    }
                }

                // remove from backfill if local backfill ticket exists
                if (m_LocalBackfillTicket != null)
                {
                    RemovePlayerFromBackfill(playerId);
                }

                // When a player leave we check if we should start backfilling
                if (ShouldBackfill() && !m_BackfillIsActive && m_AutomaticallyStartBackfillingWhenPlayerIsMissing)
                    m_ActionScheduler.ScheduleAction(async () => await StartBackfillingAsync());
            }
        }

        void RemovePlayerFromBackfill(string playerId)
        {
            Logger.LogVerbose($"RemovePlayerFromBackfill: {playerId}");

            if (!BackfillTicketIsValid())
            {
                throw new SessionException("State of the backfill ticket is invalid. Cannot add player.", SessionError.InvalidBackfillTicket);
            }

            if (!FindPlayerByIdInBackfillTicket(playerId, out var playerFromBackfill))
            {
                Logger.LogVerbose("Player not found in backfill. Cannot remove player.");
            }
            else
            {
                if (m_LocalBackfillTicket.Properties.MatchProperties.Players.Remove(playerFromBackfill))
                    m_LocalDataDirty = true;

                if (!FindPlayerTeamInBackfill(playerId, out var teamFromBackfill))
                {
                    Logger.LogVerbose(
                        "Cannot find a team for the player. Cannot remove player from the right team on the backfill.");
                }
                else
                {
                    teamFromBackfill.PlayerIds.Remove(playerId);
                    m_LocalDataDirty = true;
                    Logger.LogVerbose($"Player removed from local backfill ticket");
                    Logger.LogVerbose($"Backfilling status: {GetBackfillPlayerCount()}/{m_Session.MaxPlayers}");
                }
            }
        }

        bool FindPlayerByIdInBackfillTicket(string userID, out Unity.Services.Matchmaker.Models.Player player)
        {
            player = m_LocalBackfillTicket.Properties.MatchProperties.Players.FirstOrDefault(p => p.Id.Equals(userID));
            return player != null;
        }

        bool FindTeamByIdInLocalMatchProperties(string teamId, out Team team)
        {
            team = m_MatchProperties.Teams.FirstOrDefault(t => t.TeamId == teamId);
            return team != null;
        }

        bool FindTeamByIdInBackfillTicket(string teamId, out Team team)
        {
            team = m_LocalBackfillTicket.Properties.MatchProperties.Teams.FirstOrDefault(t => t.TeamId == teamId);
            return team != null;
        }

        bool FindPlayerTeamInBackfill(string playerId, out Team foundTeam) =>
            FindPlayerTeamFromMatchProperties(playerId, m_LocalBackfillTicket.Properties.MatchProperties,
                out foundTeam);

        bool FindPlayerTeamInLocalMatchProperties(string playerId, out Team foundTeam) =>
            FindPlayerTeamFromMatchProperties(playerId, m_MatchProperties.ToMatchProperties(), out foundTeam);

        bool FindPlayerTeamFromMatchProperties(string userID, MatchProperties matchProperties, out Team foundTeam)
        {
            foundTeam = null;
            foreach (var team in matchProperties.Teams)
            {
                if (team.PlayerIds.Contains(userID))
                {
                    foundTeam = team;
                    return true;
                }
            }

            return false;
        }

        void InitializeBackfilling()
        {
            Logger.LogVerbose($"InitializeBackfilling.");

            // hook on session events to manage backfilling
            if (m_BackfillingLoopInterval < 1)
            {
                throw new SessionException("Backfilling loop interval must be greater than 0.",
                    SessionError.InvalidOperation);
            }

            m_Session.PlayerHasLeft += OnPlayerLeft;
            m_Session.PlayerJoined += OnPlayerJoined;

            // Start backfilling loop if necessary
            Logger.LogVerbose($"StartBackfillOnInit: {m_StartBackfillOnInit}.");

            if (m_StartBackfillOnInit)
            {
                // if there is a backfill ticket Id in the matchmaking results we need to start the backfilling process
                if (MatchmakingResults.MatchProperties.BackfillTicketId != null)
                {
                    // Schedule first approval loop to have a backfill ticket to work with afterward
                    m_ApproveActionID = m_ActionScheduler.ScheduleAction(async () =>
                    {
                        var backfillTicket = await ApproveBackfillTicket(MatchmakingResults.MatchProperties.BackfillTicketId);
                        if (backfillTicket == null)
                            return;
                        m_LocalBackfillTicket = backfillTicket;
                        m_Connection = m_LocalBackfillTicket.Connection;
                        Logger.LogVerbose($"Starting backfilling. ticket ID: {m_LocalBackfillTicket.Id}");

                        // Schedule backfilling loop
                        m_ApproveActionID = null;
                        ScheduleApproveBackfillLoop();
                        m_BackfillIsActive = true;
                    }, 0);
                }
                else
                {
                    if (ShouldBackfill() && !m_BackfillIsActive)
                    {
                        Logger.LogVerbose($"Starting backfilling on init!");
                        m_ActionScheduler.ScheduleAction(async () => await StartBackfillingAsync());
                    }
                }
            }
        }

        void ScheduleApproveBackfillLoop()
        {
            if (m_ApproveActionID == null)
            {
                m_ApproveActionID = m_ActionScheduler.ScheduleAction(ApproveBackfillLoop, m_BackfillingLoopInterval);
            }
        }

        async void ApproveBackfillLoop()
        {
            m_ApproveActionID = null;

            if (!m_BackfillIsActive)
            {
                return;
            }

            if (!ShouldBackfill())
            {
                Logger.LogVerbose($"Stopping backfilling - Session is now full or locked. {GetBackfillPlayerCount()}/{m_Session.MaxPlayers}");
                Logger.LogVerbose($"Session is Locked: {m_Session.IsLocked}");
                Logger.LogVerbose($"Session is Full: {IsBackfillFull()}");

                await StopBackfillingAsync();
                return;
            }

            // At the beginning of the loop remove the player that did not connect in time
            ValidateAndRemovePlayersPendingConnection();

            if (m_LocalDataDirty)
            {
                try
                {
                    if (!BackfillTicketIsValid())
                    {
                        Logger.LogError("Backfill ticket is invalid - Cannot update backfill ticket.");
                        return;
                    }
                    Logger.LogVerbose("Updating backfill ticket.");
                    await m_MatchmakerService.UpdateBackfillTicketAsync(m_LocalBackfillTicket.Id, m_LocalBackfillTicket);
                    m_LocalDataDirty = false;
                }
                catch (Exception e)
                {
                    Logger.LogError($"Error updating backfill ticket: {e.Message}");
                }
            }
            else
            {
                try
                {
                    if (m_LocalBackfillTicket == null)
                    {
                        Logger.LogError("Local backfill ticket is null. Backfilling needs to be started first.");
                        return;
                    }

                    var backfillTicket = await ApproveBackfillTicket(m_LocalBackfillTicket.Id);
                    if (backfillTicket != null)
                        m_LocalBackfillTicket = backfillTicket;

                    // Add new players to the pending list of players waiting for connection
                    AddNewPlayersToPendingPlayers();
                }
                catch (Exception e)
                {
                    Logger.LogError($"Error approving backfill ticket: {e.Message}");
                }
            }

            if (!ShouldBackfill())
            {
                Logger.LogVerbose("Stopping backfilling - Session is now locked.");
                await StopBackfillingAsync();
                return;
            }

            ScheduleApproveBackfillLoop();
        }

        void AddNewPlayersToPendingPlayers()
        {
            if (m_PlayerConnectionTimeout == 0)
                return;

            if (!BackfillTicketIsValid())
                return;

            if (!LocalMatchPropertiesAreValid())
                return;

            // Add the new player IDs to the list of players waiting for connection
            foreach (var backfillPlayer in m_LocalBackfillTicket.Properties.MatchProperties.Players)
            {
                if (!m_MatchProperties.Players.Exists(p => p.Id == backfillPlayer.Id) && !m_PlayerWaitingConnection.ContainsKey(backfillPlayer.Id))
                {
                    Logger.LogVerbose($"Player {backfillPlayer.Id} added to the list of players waiting for connection. {m_PlayerConnectionTimeout} seconds to connect.");
                    m_PlayerWaitingConnection[backfillPlayer.Id] = DateTime.Now;
                }
            }
        }

        void ValidateAndRemovePlayersPendingConnection()
        {
            foreach (var tuple in m_PlayerWaitingConnection)
            {
                if (DateTime.Now > tuple.Value.AddSeconds(m_PlayerConnectionTimeout))
                {
                    Logger.LogVerbose($"Player {tuple.Key} did not connect in time. Removing from backfill.");
                    RemovePlayerFromBackfill(tuple.Key);
                }
            }
        }

        async Task<BackfillTicket> ApproveBackfillTicket(string backfillTicketId)
        {
            try
            {
                return await m_MatchmakerService.ApproveBackfillTicketAsync(backfillTicketId);
            }
            catch (Exception e)
            {
                Logger.LogError($"Error while approving backfill ticket: {e.Message}");
                return null;
            }
        }

        bool BackfillTicketIsValid()
        {
            if (m_LocalBackfillTicket == null)
            {
                Logger.LogVerbose("No local backfill data. Backfilling may not have started yet");
                return false;
            }

            return m_LocalBackfillTicket.Properties != null &&
                m_LocalBackfillTicket.Properties.MatchProperties != null &&
                m_LocalBackfillTicket.Properties.MatchProperties.Players != null &&
                m_LocalBackfillTicket.Properties.MatchProperties.Teams != null &&
                m_LocalBackfillTicket.Properties.MatchProperties.Teams.Count > 0;
        }

        async Task<bool> FetchMatchmakingResults()
        {
            Logger.LogVerbose("FetchMatchmakingResults");
            try
            {
                MatchmakingResults = await m_MatchmakerService.GetMatchmakingResultsAsync(m_Session.Id);
            }
            catch (Exception e)
            {
                Logger.LogError("Error while fetching matchmaking results from allocation payload: " + e.Message);
                return false;
            }

            if (MatchmakingResults == null)
            {
                Logger.LogError("Matchmaking results are null");
                return false;
            }

            if (MatchmakingResults.MatchProperties == null ||
                MatchmakingResults.MatchProperties.Teams == null ||
                MatchmakingResults.MatchProperties.Players == null)
            {
                Logger.LogError("Match properties on matchmaking results are invalid");
                return false;
            }

            m_MatchProperties = MatchmakingResults.MatchProperties;
            return true;
        }

        // Server enablement for module
        internal void SetBackfillingConfiguration(BackfillingConfiguration options)
        {
            m_StartBackfillOnInit = options.Enable;
            m_AutomaticallyRemovePlayersFromBackfill = options.AutomaticallyRemovePlayers;
            m_AutomaticallyStartBackfillingWhenPlayerIsMissing = options.AutoStart;
            m_BackfillingLoopInterval = options.BackfillingLoopInterval;
            m_PlayerConnectionTimeout = options.PlayerConnectionTimeout;
        }

        bool LocalBackfillTicketIsValid() => m_LocalBackfillTicket.Properties != null &&
        m_LocalBackfillTicket.Properties.MatchProperties != null &&
        m_LocalBackfillTicket.Properties.MatchProperties.Players != null &&
        m_LocalBackfillTicket.Properties.MatchProperties.Teams != null;

        bool LocalMatchPropertiesAreValid() =>
            m_MatchProperties.Players != null
            && m_MatchProperties.Teams != null
            && m_MatchProperties.Players != null;
    }
}
