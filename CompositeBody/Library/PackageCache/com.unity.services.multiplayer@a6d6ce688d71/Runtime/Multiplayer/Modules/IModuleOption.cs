using System;

namespace Unity.Services.Multiplayer
{
    interface IModuleOption
    {
        Type Type { get; }
        void Process(SessionHandler session);
    }
}
