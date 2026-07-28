using System;
using Unity.Services.Authentication.Internal;

namespace Unity.Services.Multiplayer
{
    class PlayerNameModuleProvider : IModuleProvider
    {
        public Type Type => typeof(PlayerNameModule);
        public int Priority => 3000;

        readonly IPlayerNameComponent m_PlayerName;

        public PlayerNameModuleProvider(IPlayerNameComponent playerName)
        {
            m_PlayerName = playerName;
        }

        public IModule Build(ISession session)
        {
            return new PlayerNameModule(m_PlayerName, session);
        }
    }
}
