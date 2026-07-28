using System;
using Unity.Services.Core.Scheduler.Internal;
using Unity.Services.Matchmaker;
using Unity.Services.Multiplayer;

namespace Unity.Services.Multiplayer
{
    class MatchmakerProvider : IModuleProvider
    {
        public Type Type => typeof(MatchmakerModule);
        public int Priority => 2000;

        readonly IActionScheduler m_ActionScheduler;
        readonly IMatchmakerService m_MatchmakerService;

        internal MatchmakerProvider(IActionScheduler actionScheduler,
                                    IMatchmakerService matchmakerService)
        {
            m_ActionScheduler = actionScheduler;
            m_MatchmakerService = matchmakerService;
        }

        public IModule Build(ISession session)
        {
            return new MatchmakerModule(session, m_ActionScheduler, m_MatchmakerService);
        }
    }
}
