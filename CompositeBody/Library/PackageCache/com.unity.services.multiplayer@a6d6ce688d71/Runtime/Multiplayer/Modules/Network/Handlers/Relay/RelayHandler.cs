using System;
using System.Threading.Tasks;
using Unity.Networking.Transport.Relay;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

namespace Unity.Services.Multiplayer
{
    interface IRelayBuilder
    {
        IRelayHandler Build();
    }

    class RelayBuilder : IRelayBuilder
    {
        readonly IRelayService m_RelayService;

        public RelayBuilder(IRelayService mRelayService)
        {
            m_RelayService = mRelayService;
        }

        public IRelayHandler Build()
        {
            return new RelayHandler(m_RelayService);
        }
    }

    interface IRelayHandler
    {
        public RelayState State { get; }
        public Guid AllocationId { get; }
        public string RelayJoinCode { get; }
        public string Region { get; }

        public Task CreateAllocationAsync(int maxPlayers, string region = null);
        public Task JoinAllocationAsync(string joinCode);
        public Task FetchJoinCodeAsync();
        public void Disconnect();
        public RelayServerData GetRelayServerData(RelayProtocol relayProtocol);
    }

    class RelayHandler : IRelayHandler
    {
        public RelayState State { get; internal set; }
        public Guid AllocationId { get; internal set; }
        public string RelayJoinCode { get; internal set; }
        public string Region { get; internal set; }

        Allocation m_Allocation;
        JoinAllocation m_JoinAllocation;

        readonly IRelayService m_RelayService;

        public RelayHandler(IRelayService relayService)
        {
            m_RelayService = relayService;
        }

        public async Task CreateAllocationAsync(int maxPlayers, string region = null)
        {
            ValidateNoAllocation();

            try
            {
                m_Allocation = await m_RelayService.CreateAllocationAsync(maxPlayers, region);
                AllocationId = m_Allocation.AllocationId;
                Region = m_Allocation.Region;
                State = RelayState.Created;
            }
            catch (RelayServiceException e)
            {
                Logger.LogError(e.Message);
                throw new SessionException("Failed to create allocation", SessionError.Unknown);
            }
        }

        public async Task JoinAllocationAsync(string joinCode)
        {
            ValidateNoAllocation();

            try
            {
                m_JoinAllocation = await m_RelayService.JoinAllocationAsync(joinCode);
                AllocationId = m_JoinAllocation.AllocationId;
                Region = m_JoinAllocation.Region;
                State = RelayState.Joined;
            }
            catch (RelayServiceException e)
            {
                Logger.LogError(e.Message);
                throw new SessionException("Failed to join allocation", SessionError.Unknown);
            }
        }

        public async Task FetchJoinCodeAsync()
        {
            ValidateAllocationExists();

            try
            {
                RelayJoinCode = await m_RelayService.GetJoinCodeAsync(AllocationId);
            }
            catch (RelayServiceException e)
            {
                Logger.LogError(e.Message);
                throw new SessionException("Failed to fetch relay join code", SessionError.Unknown);
            }
        }

        public void Disconnect()
        {
            m_Allocation = null;
            m_JoinAllocation = null;
            AllocationId = Guid.Empty;
            Region = null;
            RelayJoinCode = null;
            State = RelayState.None;
        }

        public RelayServerData GetRelayServerData(RelayProtocol relayProtocol)
        {
            ValidateAllocationExists();

            try
            {
                switch (State)
                {
                    case RelayState.Created:
                    {
                        return m_Allocation.ToRelayServerData(relayProtocol);
                    }
                    case RelayState.Joined:
                    {
                        return m_JoinAllocation.ToRelayServerData(relayProtocol);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e.Message);
                throw new SessionException("Failed to get relay server data", SessionError.Unknown);
            }

            throw new SessionException("Invalid relay state", SessionError.Unknown);
        }

        void ValidateNoAllocation()
        {
            if (m_JoinAllocation != null)
            {
                throw new SessionException("There is already a join allocation",
                    SessionError.AllocationAlreadyExists);
            }

            // With entities netcode the host also needs to join as a client
#if !ENTITIES_NETCODE_AVAILABLE
            if (m_Allocation != null)
            {
                throw new SessionException("There is already an allocation",
                    SessionError.AllocationAlreadyExists);
            }
#endif
        }

        void ValidateAllocationExists()
        {
            if (m_Allocation == null && m_JoinAllocation == null)
            {
                throw new SessionException("There is no allocation",
                    SessionError.AllocationNotFound);
            }
        }
    }
}
