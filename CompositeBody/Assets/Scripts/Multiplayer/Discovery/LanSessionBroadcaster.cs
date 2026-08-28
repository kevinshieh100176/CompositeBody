using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using XRMultiplayer;

namespace CompositeBody.Multiplayer
{
    /// <summary>
    /// Runs on the host machine only. Broadcasts a small UDP beacon on the LAN so client
    /// machines can auto-discover this session without staff typing an IP address.
    /// </summary>
    public class LanSessionBroadcaster : MonoBehaviour
    {
        public const int DiscoveryPort = 47771;
        const float k_BroadcastInterval = 1f;

        [SerializeField] string m_SessionName = "CompositeBody Session";

        UdpClient m_UdpClient;
        Thread m_BroadcastThread;
        volatile bool m_Running;

        public bool isBroadcasting => m_Running;

        public void StartBroadcasting(string sessionName, int gamePort)
        {
            if (m_Running) return;
            m_SessionName = sessionName;

            try
            {
                m_UdpClient = new UdpClient();
                m_UdpClient.EnableBroadcast = true;
            }
            catch (Exception e)
            {
                Utils.LogError($"[LanSessionBroadcaster] Failed to open broadcast socket: {e.Message}");
                return;
            }

            m_Running = true;
            m_BroadcastThread = new Thread(() => BroadcastLoop(gamePort)) { IsBackground = true };
            m_BroadcastThread.Start();
        }

        public void StopBroadcasting()
        {
            m_Running = false;
            m_BroadcastThread?.Join(200);
            m_BroadcastThread = null;
            m_UdpClient?.Close();
            m_UdpClient = null;
        }

        void BroadcastLoop(int gamePort)
        {
            var endpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
            while (m_Running)
            {
                try
                {
                    int playerCount = GameSessionManager.Instance != null ? GameSessionManager.Instance.connectedPlayerCount : 0;
                    var state = GameSessionManager.Instance != null ? GameSessionManager.Instance.sessionState : SessionState.Lobby;

                    string payload = LanSessionInfo.Encode(new LanSessionInfo
                    {
                        sessionName = m_SessionName,
                        port = gamePort,
                        currentPlayers = playerCount,
                        maxPlayers = GameSessionManager.MaxPlayers,
                        state = state
                    });

                    byte[] data = Encoding.UTF8.GetBytes(payload);
                    m_UdpClient.Send(data, data.Length, endpoint);
                }
                catch (Exception e)
                {
                    Utils.Log($"[LanSessionBroadcaster] Broadcast error: {e.Message}", 1);
                }

                Thread.Sleep((int)(k_BroadcastInterval * 1000));
            }
        }

        void OnDestroy() => StopBroadcasting();
        void OnApplicationQuit() => StopBroadcasting();
    }
}
