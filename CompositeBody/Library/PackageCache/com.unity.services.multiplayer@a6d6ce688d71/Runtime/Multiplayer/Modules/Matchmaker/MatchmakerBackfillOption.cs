using System;

namespace Unity.Services.Multiplayer
{
    class MatchmakerBackfillOption : IModuleOption
    {
        readonly BackfillingConfiguration m_Options;

        public MatchmakerBackfillOption(BackfillingConfiguration options)
        {
            m_Options = options;
        }

        public Type Type => typeof(MatchmakerBackfillOption);

        public void Process(SessionHandler session)
        {
            var module = session.GetModule<MatchmakerModule>();
            if (module == null)
            {
                throw new SessionException(
                    "Trying to setup connection in session but the module isn't registered.", SessionError.MatchmakerModuleMissing);
            }

            if (session.IsServer)
            {
                module.SetBackfillingConfiguration(m_Options);
            }
            else
            {
                throw new SessionException("Attempting to set backfilling configuration on a session handle that is not controlled by a server", SessionError.InvalidOperation);
            }
        }
    }
}
