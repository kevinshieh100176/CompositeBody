namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// <para>Indicates for whom the property should be visible.</para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="VisibilityPropertyOptions.Public">public</see> property is visible to everyone and is included in query results.<br/>
    /// A <see cref="VisibilityPropertyOptions.Member">member</see> property is only visible to the members of the session (in other words, those who have successfully joined).<br/>
    /// A <see cref="VisibilityPropertyOptions.Private">private</see> property is only visible to the member who set it. Only the host can set and see private session properties.
    /// <br/>
    /// For more information, see <a href="https://docs.unity.com/ugs/en-us/manual/lobby/manual/lobby-data-and-player-data#Data_access_table">data access table</a>.<br/>
    /// <br/>
    /// Use in conjunction with the following methods:
    /// <br/>• <see cref="IHostSession.SetProperty"/>
    /// <br/>• <see cref="IHostSession.SetProperties"/>
    /// <br/>• <see cref="IPlayer.SetProperty"/>
    /// <br/>• <see cref="IPlayer.SetProperties"/>
    /// </para>
    /// </remarks>
    /// <example>
    /// <para>Creating a public session property</para>
    /// <code>
    /// var publicSessionProperty = new SessionProperty("value", VisibilityPropertyOptions.Public);
    /// </code>
    /// <para>Creating a private player property</para>
    /// <code>
    /// var privatePlayerProperty = new PlayerProperty("value", VisibilityPropertyOptions.Private);
    /// </code>
    /// </example>
    /// <seealso cref="SessionProperty"/>
    /// <seealso cref="PlayerProperty"/>
    public enum VisibilityPropertyOptions
    {
        /// <summary>
        /// Enum Public for value: public
        /// </summary>
        Public = 1,
        /// <summary>
        /// Enum Member for value: member
        /// </summary>
        Member = 2,
        /// <summary>
        /// Enum Private for value: private
        /// </summary>
        Private = 3
    }
}
