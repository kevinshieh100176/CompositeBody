using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using XRMultiplayer;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// Runs on client machines. Listens for <see cref="LanSessionBroadcaster"/> beacons and keeps
    /// a live list of sessions currently visible on the LAN, expiring any that stop broadcasting.
    /// </summary>
    public class LanSessionDiscovery : MonoBehaviour
    {
        const float k_SessionTimeout = 3f;

        UdpClient m_UdpClient;
        Thread m_ListenThread;
        volatile bool m_Running;

        readonly object m_Lock = new();
        readonly Dictionary<string, (LanSessionInfo info, float lastSeenRealtime)> m_Sessions = new();
        readonly Queue<(string address, string payload)> m_PendingPackets = new();

        /// <summary>Raised on the main thread whenever the discovered-session list changes.</summary>
        public event Action<IReadOnlyList<LanSessionInfo>> onSessionsUpdated;

        public void StartListening()
        {
            if (m_Running) return;

            try
            {
                m_UdpClient = new UdpClient();
                m_UdpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                m_UdpClient.Client.Bind(new IPEndPoint(IPAddress.Any, LanSessionBroadcaster.DiscoveryPort));
            }
            catch (Exception e)
            {
                Utils.LogError($"[LanSessionDiscovery] Failed to open listen socket: {e.Message}");
                return;
            }

            m_Running = true;
            m_ListenThread = new Thread(ListenLoop) { IsBackground = true };
            m_ListenThread.Start();
        }

        public void StopListening()
        {
            m_Running = false;
            m_UdpClient?.Close();
            m_ListenThread?.Join(200);
            m_ListenThread = null;
            lock (m_Lock) m_Sessions.Clear();
        }

        void ListenLoop()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (m_Running)
            {
                try
                {
                    byte[] data = m_UdpClient.Receive(ref remote);
                    string payload = Encoding.UTF8.GetString(data);
                    lock (m_Lock) m_PendingPackets.Enqueue((remote.Address.ToString(), payload));
                }
                catch (SocketException)
                {
                    // Thrown by Close() unblocking Receive(); expected on shutdown.
                }
                catch (Exception e)
                {
                    Utils.Log($"[LanSessionDiscovery] Listen error: {e.Message}", 1);
                }
            }
        }

        void Update()
        {
            if (!m_Running) return;

            bool changed = false;

            lock (m_Lock)
            {
                while (m_PendingPackets.Count > 0)
                {
                    var (address, payload) = m_PendingPackets.Dequeue();
                    if (LanSessionInfo.TryDecode(payload, address, out var info))
                    {
                        m_Sessions[address] = (info, Time.realtimeSinceStartup);
                        changed = true;
                    }
                }

                List<string> expired = null;
                foreach (var kvp in m_Sessions)
                {
                    if (Time.realtimeSinceStartup - kvp.Value.lastSeenRealtime > k_SessionTimeout)
                    {
                        expired ??= new List<string>();
                        expired.Add(kvp.Key);
                    }
                }
                if (expired != null)
                {
                    foreach (string key in expired) m_Sessions.Remove(key);
                    changed = true;
                }
            }

            if (changed) NotifyListeners();
        }

        void NotifyListeners()
        {
            var list = new List<LanSessionInfo>();
            lock (m_Lock)
            {
                foreach (var kvp in m_Sessions) list.Add(kvp.Value.info);
            }
            onSessionsUpdated?.Invoke(list);
        }

        void OnDestroy() => StopListening();
        void OnApplicationQuit() => StopListening();
    }
}
