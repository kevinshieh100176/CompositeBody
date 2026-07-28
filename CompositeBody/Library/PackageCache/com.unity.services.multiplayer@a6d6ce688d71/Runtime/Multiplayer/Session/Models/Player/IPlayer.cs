using System.Collections.Generic;

namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// An interface that allows to modify the properties of a Player.
    /// </summary>
    public interface IPlayer : IReadOnlyPlayer
    {
        /// <summary>
        /// Set the <paramref name="allocationId"/> returned by the networking
        /// solution which associates this player in this session with a
        /// persistent connection.
        /// </summary>
        /// <param name="allocationId">This value is used to identify the associated member in a session.</param>
        public void SetAllocationId(string allocationId);

        /// <summary>
        /// Modifies multiple <see cref="PlayerProperty">properties</see> of the
        /// player.
        /// </summary>
        /// <param name="properties">A dictionary of <see
        /// cref="PlayerProperty">player properties</see> to be added, updated
        /// or removed.</param>
        /// <seealso cref="SetProperty"/>
        public void SetProperties(Dictionary<string, PlayerProperty> properties);

        /// <summary>
        /// Modifies a single <see cref="PlayerProperty"/> of the <see
        /// cref="IPlayer">player</see>.
        /// </summary>
        /// <param name="key">The <see cref="PlayerProperty">player
        /// property</see>'s key.</param>
        /// <param name="property">The <see cref="PlayerProperty">player
        /// property</see>'s value.</param>
        /// <remarks>
        /// To set the value of the property to <see langword="null"/>, pass a
        /// <see cref="PlayerProperty"/> with its <see
        /// cref="PlayerProperty.Value"/> set to <see langword="null"/>.
        /// <br/>
        /// To remove an existing property, pass <see langword="null"/> to the
        /// <paramref name="property"/> argument.
        /// </remarks>
        /// <example>
        /// <para>To add a <c>colour</c> property</para>
        /// <code>var player = mySession.CurrentPlayer;
        /// var redColourProperty = new PlayerProperty("red");
        /// player.SetProperty("colour", redColourProperty);
        /// await mySession.SaveCurrentPlayerDataAsync();</code>
        /// <para>To update the <c>colour</c> property to <see langword="null"/></para>
        /// <code>var player = mySession.CurrentPlayer;
        /// var nullColourProperty = new PlayerProperty(null);
        /// player.SetProperty("colour", nullColourProperty);
        /// await mySession.SaveCurrentPlayerDataAsync();</code>
        /// <para>To remove the <c>colour</c> property</para>
        /// <code>var player = mySession.CurrentPlayer;
        /// player.SetProperty("colour", null);
        /// await mySession.SaveCurrentPlayerDataAsync();</code>
        /// </example>
        public void SetProperty(string key, PlayerProperty property);
    }
}
