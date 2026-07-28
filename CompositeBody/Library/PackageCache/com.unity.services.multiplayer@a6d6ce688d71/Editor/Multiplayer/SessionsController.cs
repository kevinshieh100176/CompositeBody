using System;
using System.Collections.Generic;
using Unity.Services.Core;
using UnityEngine;

namespace Unity.Services.Multiplayer.Editor
{
    class SessionsController
    {
        public event Action SessionsChanged;

        public bool Initialized { get; private set; }
        public IMultiplayerService Service { get; private set; }
        public IReadOnlyDictionary<string, ISession> Sessions => Service?.Sessions;

        public SessionsController()
        {
        }

        public void Initialize()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                UnityServices.Initialized -= InitializeService;
                UnityServices.Initialized += InitializeService;
            }
            else
            {
                InitializeService();
            }
        }

        public void Reset()
        {
            if (Service == null)
                return;

            Service.SessionAdded -= OnSessionAdded;
            Service.SessionRemoved -= OnSessionRemoved;
            Service = null;
            SessionsChanged?.Invoke();
            Initialized = false;
        }

        void InitializeService()
        {
            Service = MultiplayerService.Instance;
            Service.SessionAdded += OnSessionAdded;
            Service.SessionRemoved += OnSessionRemoved;
            Initialized = true;
            SessionsChanged?.Invoke();
        }

        void OnSessionAdded(ISession session)
        {
            session.Changed += InvokeSessionsChanged;
            session.Network.StateChanged += OnNetworkChanged;
            InvokeSessionsChanged();
        }

        void OnSessionRemoved(ISession session)
        {
            session.Changed -= InvokeSessionsChanged;
            session.Network.StateChanged -= OnNetworkChanged;
            InvokeSessionsChanged();
        }

        void InvokeSessionsChanged()
        {
            SessionsChanged?.Invoke();
        }

        void OnNetworkChanged(NetworkState state)
        {
            Debug.Log($"Network state changed: {state}");
            InvokeSessionsChanged();
        }
    }
}
