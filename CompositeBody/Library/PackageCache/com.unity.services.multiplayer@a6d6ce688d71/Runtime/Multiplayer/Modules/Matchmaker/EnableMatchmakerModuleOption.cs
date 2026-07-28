using System;

namespace Unity.Services.Multiplayer
{
    class EnableMatchmakerModuleOption : IModuleOption
    {
        public Type Type => typeof(EnableMatchmakerModuleOption);

        public EnableMatchmakerModuleOption()
        {
        }

        public void Process(SessionHandler session)
        {
            var module = session.GetModule<MatchmakerModule>();

            if (module == null)
            {
                throw new SessionException(
                    "Trying to setup connection in session but the module isn't registered.", SessionError.MatchmakerModuleMissing);
            }

            module.Enable();
        }
    }
}
