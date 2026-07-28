namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Represents a property of a session.
    /// </summary>
    public class SessionProperty : IValueProperty, IVisibilityProperty
    {
        /// <summary>
        /// The value of the property.
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// The visibility of the property.
        /// </summary>
        public VisibilityPropertyOptions Visibility { get; }

        /// <summary>
        /// The index of the property for querying purposes.
        /// </summary>
        public PropertyIndex Index { get; }

        /// <summary>
        /// Creates a new SessionProperty.
        /// </summary>
        /// <param name="value">The value of the property.</param>
        /// <param name="visibility">The visibility of the property.</param>
        /// <param name="index">The index of the property for querying purposes.</param>
        public SessionProperty(string value, VisibilityPropertyOptions visibility = VisibilityPropertyOptions.Public,
            PropertyIndex index = PropertyIndex.None)
        {
            Value = value;
            Visibility = visibility;
            Index = index;
        }
    }
}
