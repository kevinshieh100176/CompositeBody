using System.Threading.Tasks;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Component providing the multiplayer session module
    /// </summary>
    interface IModule
    {
        public Task InitializeAsync();
        public Task LeaveAsync();
    }
}
