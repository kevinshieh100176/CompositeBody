namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Represents a property of a player.
    /// </summary>
    public class PlayerProperty : IValueProperty, IVisibilityProperty
    {
        /// <summary>
        /// Creates a new PlayerProperty.
        /// </summary>
        /// <param name="value">The value of the property.</param>
        /// <param name="visibility">The visibility of the property.</param>
        public PlayerProperty(string value = default, VisibilityPropertyOptions visibility = VisibilityPropertyOptions.Public)
        {
            Value = value;
            Visibility = visibility;
        }

        /// <summary>
        /// The value of the property.
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// The visibility of the property.
        /// </summary>
        public VisibilityPropertyOptions Visibility { get; }
    }
}
