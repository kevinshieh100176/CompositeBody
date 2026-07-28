using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Core;
using UnityEditor;
using UnityEngine;

namespace Unity.Services.Multiplayer.Editor
{
    static class SessionsCleaner
    {
        const string k_EnclosingType = nameof(SessionsCleaner);

        /// <summary>
        /// Since NGO <see cref="Netcode.NetworkManager"/> registers during
        /// its awake method, we need to assure that we register for changes
        /// to playmode states before it to stop any active sessions.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterPlaymodeChangeState()
        {
            Logger.LogCallVerboseWithMessage(k_EnclosingType, "Registering for playmode state changes...");
            EditorApplication.playModeStateChanged -= PlayModeStateChanged;
            EditorApplication.playModeStateChanged += PlayModeStateChanged;
        }

        /// <summary>
        /// This method ensures that all sessions
        /// are being left when leaving Playmode.
        /// </summary>
        /// <remarks>
        /// It is important to leave all sessions to avoid the cloud
        /// service waiting on reconnecting players when in playmode.
        /// <br/>
        /// Sessions using any UTP have to disconnect explicitly
        /// to release the lock on the <c>ip:port</c> used
        /// so it can be locked again for the next session.
        /// </remarks>
        static async void PlayModeStateChanged(PlayModeStateChange change)
        {
            if (change is not PlayModeStateChange.ExitingPlayMode)
            {
                return;
            }

            Logger.LogCallVerboseWithMessage(k_EnclosingType, "Exiting playmode cleanup.");
            if (UnityServices.Services is not { Count : > 0 })
            {
                Logger.LogCallVerboseWithMessage(k_EnclosingType, "No Services running. Done.");
                return;
            }

            try
            {
                var leaveSessionTasks = new List<Task>();
                foreach (var services in UnityServices.Services.Values)
                {
                    Logger.LogCallVerboseWithMessage(k_EnclosingType, $"Cleaning up Services Id {services.GetIdentifier()}.");
                    var multiplayer = services.GetService<IMultiplayerService>();
                    if (multiplayer is null)
                    {
                        Logger.LogCallVerboseWithMessage(k_EnclosingType,
                            $"No {nameof(MultiplayerService)} found in {services.GetIdentifier()}.");
                        continue;
                    }

                    foreach (var session in multiplayer.Sessions.Values)
                    {
                        Logger.LogCallVerboseWithMessage(k_EnclosingType, $"Adding session \"{session.Id}\" to the list of sessions to leave due to playmode exiting.");
                        leaveSessionTasks.Add(session.LeaveAsync());
                    }
                }

                await Task.WhenAll(leaveSessionTasks);
                Logger.LogCallVerboseWithMessage(k_EnclosingType, "All sessions left.");
            }
            catch (Exception e)
            {
                Logger.LogCallError(k_EnclosingType, $"Error while cleaning up sessions: {e.Message}");
            }
        }
    }
}
