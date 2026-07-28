using Unity.Services.Core.Scheduler.Internal;

namespace Unity.Services.Multiplayer
{
    internal interface INetworkBuilder
    {
        public INetworkHandler Build();
    }

    internal class NetworkBuilder : INetworkBuilder
    {
        readonly IActionScheduler m_ActionScheduler;

        public NetworkBuilder(IActionScheduler actionScheduler)
        {
            m_ActionScheduler = actionScheduler;
        }

        public INetworkHandler Build()
        {
            switch (NetcodeUtils.Current)
            {
                case NetcodeType.GameObjects:
#if GAMEOBJECTS_NETCODE_AVAILABLE
                    return new GameObjectsNetcodeNetworkHandler(m_ActionScheduler);
#else
                    throw new SessionException("Netcode for GameObjects package is not installed", SessionError.MissingAssembly);
#endif
                case NetcodeType.Entities:
#if ENTITIES_NETCODE_AVAILABLE && !UNITY_WEBGL
                    return new EntitiesNetcodeNetworkHandler(m_ActionScheduler);
#else
                    throw new SessionException(
                        "Netcode for Entities package is not installed",
                        SessionError.MissingAssembly);
#endif
                default:
                    throw new SessionException("Netcode for GameObjects or Netcode for Entities package need to be installed", SessionError.MissingAssembly);
            }
        }
    }
}
