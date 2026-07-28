using System;
using System.Linq;
using System.Threading.Tasks;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication.Internal;
using Unity.Services.DistributedAuthority;
using Unity.Services.DistributedAuthority.Exceptions;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

namespace Unity.Services.Multiplayer
{
    interface IDaBuilder
    {
        IDaHandler Build();
    }

    class DaBuilder : IDaBuilder
    {
        readonly IRelayService m_RelayService;
        readonly IDistributedAuthorityService m_DaService;
        readonly IPlayerId m_PlayerId;

        public DaBuilder(IRelayService mRelayService, IDistributedAuthorityService mDaService, IPlayerId playerId)
        {
            m_RelayService = mRelayService;
            m_DaService = mDaService;
            m_PlayerId = playerId;
        }

        public IDaHandler Build()
        {
            return new DaHandler(m_RelayService, m_DaService, m_PlayerId);
        }
    }

    interface IDaHandler
    {
        public string RelayJoinCode { get; }
        public string Region { get; }
        public Guid AllocationId { get; }
        public Task CreateAndJoinSessionAsync(string lobbyId, string region);
        public Task JoinSessionAsync(string relayJoinCode);
        public RelayServerData GetRelayServerData(RelayProtocol connectionType);
        public ConnectPayload GetConnectPayload();
    }

    class DaHandler : IDaHandler
    {
        public string RelayJoinCode { get; internal set; }
        public string Region { get; internal set; }

        public Guid AllocationId
        {
            get
            {
                if (m_JoinAllocation == null)
                {
                    return Guid.Empty;
                }

                return m_JoinAllocation.AllocationId;
            }
        }

        JoinAllocation m_JoinAllocation;

        readonly IRelayService m_RelayService;
        readonly IDistributedAuthorityService m_DaService;
        readonly IPlayerId m_PlayerId;

        public DaHandler(IRelayService relayService, IDistributedAuthorityService daService, IPlayerId playerId)
        {
            m_RelayService = relayService;
            m_DaService = daService;
            m_PlayerId = playerId;
        }

        public async Task CreateAndJoinSessionAsync(string lobbyId, string region)
        {
            ValidateNoAllocation();

            try
            {
                await m_DaService.CreateSessionForLobbyIdAsync(lobbyId, region);
                var relayJoinCode = await m_DaService.JoinSessionForLobbyIdAsync(lobbyId);
                await JoinSessionAsync(relayJoinCode);
            }
            catch (DistributedAuthorityServiceException e)
            {
                Logger.LogError(e.Message);
                throw new SessionException("Failed to create and join session", SessionError.Unknown);
            }
        }

        public async Task JoinSessionAsync(string relayJoinCode)
        {
            ValidateNoAllocation();

            try
            {
                RelayJoinCode = relayJoinCode;
                m_JoinAllocation = await m_RelayService.JoinAllocationAsync(RelayJoinCode);
                Region = m_JoinAllocation.Region;
            }
            catch (DistributedAuthorityServiceException e)
            {
                Logger.LogError(e.Message);
                throw new SessionException("Failed to join session", SessionError.Unknown);
            }
            catch (Exception e)
            {
                Logger.LogError(e.Message);
                throw new SessionException("Failed to join session", SessionError.Unknown);
            }
        }

        public RelayServerData GetRelayServerData(RelayProtocol connectionType)
        {
            ValidateAllocationExists();

            try
            {
                return m_JoinAllocation.ToRelayServerData(connectionType);
            }
            catch (Exception e)
            {
                Logger.LogError(e.Message);
                throw new SessionException("Failed to get relay server data", SessionError.Unknown);
            }

            throw new SessionException("Invalid relay state", SessionError.Unknown);
        }

        public ConnectPayload GetConnectPayload()
        {
            if (string.IsNullOrEmpty(m_PlayerId.PlayerId))
            {
                throw new SessionException("Player must be authenticated to get connection payload", SessionError.Unknown);
            }

            return new ConnectPayload(m_PlayerId.PlayerId);
        }

        void ValidateNoAllocation()
        {
            if (m_JoinAllocation != null)
            {
                throw new SessionException("There is already an allocation", SessionError.AllocationAlreadyExists);
            }
        }

        void ValidateAllocationExists()
        {
            if (m_JoinAllocation == null)
            {
                throw new SessionException("There is no allocation", SessionError.AllocationNotFound);
            }
        }
    }
}
