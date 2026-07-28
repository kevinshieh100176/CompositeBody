using System.Collections.Generic;
using Unity.Services.Lobbies.Models;

namespace Unity.Services.Lobbies
{
    /// <summary>
    /// <para><see cref="PlayerDataObject"/> to update on a given <see
    /// cref="PlayerUpdateRequest">player update request</see>.</para>
    /// <para>Used in conjunction with <see
    /// cref="ILobbyService.UpdatePlayerAsync"/>.</para>
    /// </summary>
    public class UpdatePlayerOptions
    {
        /// <summary>
        /// An ID that associates this player in this lobby with a persistent
        /// connection. When a disconnect notification is received, this value
        /// is used to identify the associated player in a lobby to mark them as
        /// disconnected.
        /// </summary>
        public string AllocationId { get; set; }

        /// <summary>
        /// Connection information for connecting to a relay with this player.
        /// </summary>
        public string ConnectionInfo { get; set; }

        /// <summary>
        /// Custom game-specific properties to add, update or remove from the
        /// player (for example, role or skill).
        /// </summary>
        /// <remarks>
        /// To remove an existing player data, include it in <see
        /// cref="Player.Data"/> but set property object to <see
        /// langword="null"/>.
        /// <br/>
        /// To update the value to <see langword="null"/>, set the <see
        /// cref="PlayerDataObject.Value"/> of the player data object
        /// to <see langword="null"/>.
        /// </remarks>
        /// <example>
        /// <para>To add a <c>colour</c> player data</para>
        /// <code>var redColourPlayerData = new PlayerDataObject(VisibilityOptions.Public, "red")
        /// var data = new Dictionary&lt;string, PlayerDataObject&gt; { ["colour"] = redColourPlayerData };
        /// LobbyService.Instance.UpdatePlayerAsync("lobbyId", "playerId", data);</code>
        /// <para>To update the <c>colour</c> player data to <see langword="null"/></para>
        /// <code>var nullPlayerDataObject = new PlayerDataObject(VisibilityOptions.Public, null)
        /// var data = new Dictionary&lt;string, PlayerDataObject&gt; { ["colour"] = nullPlayerDataObject };
        /// LobbyService.Instance.UpdatePlayerAsync("lobbyId", "playerId", data);</code>
        /// <para>To remove the <c>colour</c> player data</para>
        /// <code>var data = new Dictionary&lt;string, PlayerDataObject&gt; { ["colour"] = null };
        /// LobbyService.Instance.UpdatePlayerAsync("lobbyId", "playerId", data); </code>
        /// </example>
        /// <seealso cref=
        /// "PlayerDataObject(PlayerDataObject.VisibilityOptions, string)"/>
        public Dictionary<string, PlayerDataObject> Data { get; set; }
    }
}
