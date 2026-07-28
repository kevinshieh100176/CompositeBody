using Unity.Netcode;
using UnityEngine;
using UnityEngine.Scripting;
using System.Threading;
#if !UNITY_NETCODE_GAMEOBJECTS_2_1_0_ABOVE
using System.Threading.Tasks;
using Unity.Multiplayer.Tools.Common;
#endif

[assembly: AlwaysLinkAssembly]
namespace Unity.Multiplayer.Tools.Adapters.Ngo1
{
    static class Ngo1AdapterInitializer
    {
        static bool s_Initialized;
        static Ngo1Adapter s_Instance;
        static CancellationTokenSource s_InitializeAdapterCts;
        
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        internal static void InitializeAdapter()
        {
            if (s_Instance != null)
            {
                NetworkAdapters.RemoveAdapter(s_Instance);
                s_Instance.Deinitialize();
                s_Instance = null;
            }
#if UNITY_NETCODE_GAMEOBJECTS_2_1_0_ABOVE
            if (s_Initialized)
            {
                NetworkManager.OnInstantiated -= OnInstantiated;
                NetworkManager.OnDestroying -= OnDestroying;
            }
            // We need the OnInstantiated callback because the NetworkManager could get destroyed and recreated when we change scenes
            // OnInstantiated is called in Awake, and the GetNetworkManagerAsync only returns at least after OnEnable
            // therefore the initialization is not called twice
            NetworkManager.OnInstantiated += OnInstantiated;
            NetworkManager.OnDestroying += OnDestroying;
            s_Initialized = true;
#else
            s_InitializeAdapterCts?.Cancel();
            s_InitializeAdapterCts = new CancellationTokenSource();
            InitializeAdapterAsync(s_InitializeAdapterCts.Token).Forget();
#endif
        }

#if UNITY_NETCODE_GAMEOBJECTS_2_1_0_ABOVE
        private static void OnInstantiated(NetworkManager networkManager)
        {
            if (s_Instance == null)
            {
                s_Instance = new Ngo1Adapter(networkManager);
                NetworkAdapters.AddAdapter(s_Instance);
            }
            else
                s_Instance.ReplaceNetworkManager(networkManager);

        }

        private static void OnDestroying(NetworkManager _)
        {
            if (s_Instance != null)
            {
                NetworkAdapters.RemoveAdapter(s_Instance);
                s_Instance.Deinitialize();
                s_Instance = null;
            }
        }
#else
        
        static async Task InitializeAdapterAsync(CancellationToken ct)
        {
            var networkManager = await GetNetworkManagerAsync(ct);
            if(ct.IsCancellationRequested)
                return;
            s_Instance = new Ngo1Adapter(networkManager);
            NetworkAdapters.AddAdapter(s_Instance);
        }

        static async Task<NetworkManager> GetNetworkManagerAsync(CancellationToken ct)
        {
            while (NetworkManager.Singleton == null || NetworkManager.Singleton.NetworkTickSystem == null)
            {
                if (ct.IsCancellationRequested)
                    return null;
                await Task.Yield();
            }

            return NetworkManager.Singleton;
        }
#endif
    }
}
