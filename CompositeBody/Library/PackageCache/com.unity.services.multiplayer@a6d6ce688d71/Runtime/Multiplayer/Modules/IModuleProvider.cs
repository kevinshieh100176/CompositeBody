using System;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Component providing the multiplayer session module
    /// </summary>
    interface IModuleProvider
    {
        /// <summary>
        /// The type of the module provided
        /// </summary>
        public Type Type { get; }

        /// <summary>
        /// Higher gets executed first
        /// </summary>
        public int Priority { get; }

        /// <summary>
        /// Creates the module instance
        /// </summary>
        /// <param name="session">The session to build the instance for</param>
        /// <returns>The created module</returns>
        public IModule Build(ISession session);
    }
}
