using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.Services.Multiplayer
{
    class ModuleRegistry : IModuleRegistry
    {
        List<IModuleProvider> IModuleRegistry.ModuleProviders
        {
            get
            {
                var providers = ModuleProviders.Values.ToList();
                providers.Sort((x, y) => x.Priority.CompareTo(y.Priority));
                return providers;
            }
        }

        internal readonly Dictionary<Type, IModuleProvider> ModuleProviders = new Dictionary<Type, IModuleProvider>();

        internal ModuleRegistry()
        {
        }

        public void RegisterModuleProvider(IModuleProvider moduleProvider)
        {
            if (!ModuleProviders.TryAdd(moduleProvider.Type, moduleProvider))
            {
                Logger.LogWarning($"Failed to register module provider for module type:'{moduleProvider.Type.Name}'");
            }
        }
    }
}
