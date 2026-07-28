// WARNING: Auto generated code. Modifications will be lost!

using System;

namespace Unity.Services.Multiplayer.Editor.Shared.DependencyInversion
{
    /// <summary>
    /// The scoped provider creates services instantiated under its scope so
    /// so that if they require to be disposed they will along this provider
    /// </summary>
    public interface IScopedServiceProvider : IServiceProvider, IDisposable
    {
        /// <summary>
        /// Creates a scoped provider
        /// </summary>
        /// <returns>The scoped provider</returns>
        IScopedServiceProvider CreateScope();
    }
}
