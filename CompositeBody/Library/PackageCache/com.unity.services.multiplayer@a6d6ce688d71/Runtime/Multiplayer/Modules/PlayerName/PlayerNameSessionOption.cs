using System;

namespace Unity.Services.Multiplayer
{
    class PlayerNameSessionOption : IModuleOption
    {
        Type IModuleOption.Type => typeof(PlayerNameModule);

        public VisibilityPropertyOptions Visibility { get; private set; }

        internal PlayerNameSessionOption(VisibilityPropertyOptions visibility)
        {
            Visibility = visibility;
        }

        public void Process(SessionHandler session)
        {
            var module = session.GetModule<PlayerNameModule>();
            module?.Enable(this);
        }
    }
}
