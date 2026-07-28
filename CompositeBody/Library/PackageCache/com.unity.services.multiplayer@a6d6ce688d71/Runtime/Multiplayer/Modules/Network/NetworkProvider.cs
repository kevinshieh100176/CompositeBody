using System;

namespace Unity.Services.Multiplayer
{
    class NetworkProvider : IModuleProvider
    {
        public Type Type => typeof(NetworkModule);
        public int Priority => 1000;

        readonly INetworkBuilder m_NetworkBuilder;
        readonly IRelayBuilder m_RelayBuilder;
        readonly IDaBuilder m_DaBuilder;

        internal NetworkProvider(INetworkBuilder networkBuilder, IDaBuilder daBuilder, IRelayBuilder relayBuilder)
        {
            m_NetworkBuilder = networkBuilder;
            m_DaBuilder = daBuilder;
            m_RelayBuilder = relayBuilder;
        }

        public IModule Build(ISession session)
        {
            return new NetworkModule(session, m_NetworkBuilder, m_DaBuilder, m_RelayBuilder);
        }
    }
}
