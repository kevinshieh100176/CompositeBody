using System;
using System.Collections.Generic;
using Unity.Services.Lobbies.Models;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// ISessionInfo represents information about a session.
    /// </summary>
    public interface ISessionInfo
    {
        /// <summary>
        /// The session name
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The session ID
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// The Unity project ID
        /// </summary>
        public string Upid { get; }

        /// <summary>
        /// The session host ID
        /// </summary>
        public string HostId { get; }

        /// <summary>
        /// Available slots in the session
        /// </summary>
        public int AvailableSlots { get; }

        /// <summary>
        /// The maximum number of players allowed to be part of the session
        /// </summary>
        public int MaxPlayers { get; }

        /// <summary>
        /// True if the session is Locked (eg. does not allow more players to join), false otherwise
        /// </summary>
        public bool IsLocked { get; }

        /// <summary>
        /// True if the session has a password, false otherwise
        /// </summary>
        public bool HasPassword { get; }

        /// <summary>
        /// The date and time the session was updated last
        /// </summary>
        public DateTime LastUpdated { get; }

        /// <summary>
        /// The date and time the session was created
        /// </summary>
        public DateTime Created { get; }

        /// <summary>
        /// Properties of the session set by the host.
        /// </summary>
        public IReadOnlyDictionary<string, SessionProperty> Properties { get; }
    }

    class LobbySessionInfo : ISessionInfo
    {
        Lobby Lobby;

        public IReadOnlyDictionary<string, SessionProperty> Properties {get;}
        internal LobbySessionInfo(Lobby lobby)
        {
            Lobby = lobby;
            Properties = lobby.Data != null ? ConvertProperty(lobby.Data, LobbyConverter.ToSessionProperty) : null;
        }

        public string Name { get => Lobby.Name; set => Lobby.Name = value; }
        public string Id { get => Lobby.Id; set => Lobby.Id = value; }
        public string Upid { get => Lobby.Upid; set => Lobby.Upid = value; }
        public int MaxPlayers { get => Lobby.MaxPlayers; set => Lobby.MaxPlayers = value; }
        public int AvailableSlots { get => Lobby.AvailableSlots; set => Lobby.AvailableSlots = value; }
        public bool IsLocked { get => Lobby.IsLocked; set => Lobby.IsLocked = value; }
        public bool HasPassword { get => Lobby.HasPassword; set => Lobby.HasPassword = value; }
        public DateTime LastUpdated { get => Lobby.LastUpdated; set => Lobby.LastUpdated = value; }
        public string HostId { get => Lobby.HostId; set => Lobby.HostId = value; }
        public DateTime Created { get => Lobby.Created; set => Lobby.Created = value; }


        static Dictionary<string, TResult> ConvertProperty<TSource, TResult>(
            Dictionary<string, TSource> source, Func<TSource, TResult> func)
        {
            var result = new Dictionary<string, TResult>(source.Count);

            foreach (var(key, dataObject) in source)
            {
                result.Add(key, func(dataObject));
            }

            return result;
        }
    }
}
