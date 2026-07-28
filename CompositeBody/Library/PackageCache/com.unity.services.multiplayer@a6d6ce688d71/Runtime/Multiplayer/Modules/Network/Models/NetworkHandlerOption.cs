using System;

namespace Unity.Services.Multiplayer
{
    class NetworkHandlerOption : IModuleOption
    {
        public Type Type => typeof(NetworkModule);
        INetworkHandler NetworkHandler { get; }

        public NetworkHandlerOption(INetworkHandler networkHandler)
        {
            NetworkHandler = networkHandler;
        }

        public void Process(SessionHandler session)
        {
            var module = session.GetModule<NetworkModule>();
            if (module == null)
            {
                throw new Exception("Trying to setup session network but the module isn't registered.");
            }

            module.NetworkHandler = NetworkHandler;
        }
    }
}
