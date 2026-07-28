using System.Collections.Generic;

namespace Unity.Services.Multiplayer
{
    interface IModuleRegistry
    {
        List<IModuleProvider> ModuleProviders { get; }
        void RegisterModuleProvider(IModuleProvider moduleProvider);
    }
}
