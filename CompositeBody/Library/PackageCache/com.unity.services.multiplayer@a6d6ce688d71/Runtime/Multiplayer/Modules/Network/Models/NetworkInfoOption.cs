using System;

namespace Unity.Services.Multiplayer
{
    class NetworkInfoOption : IModuleOption
    {
        public Type Type => typeof(NetworkModule);
        internal NetworkInfo Options { get; }

        public NetworkInfoOption(NetworkInfo options)
        {
            Options = options;
        }

        public void Process(SessionHandler session)
        {
            var module = session.GetModule<NetworkModule>();

            if (module == null)
            {
                throw new Exception("Trying to setup network in session but the module isn't registered.");
            }

            module.NetworkInfo = Options;
        }
    }
}
