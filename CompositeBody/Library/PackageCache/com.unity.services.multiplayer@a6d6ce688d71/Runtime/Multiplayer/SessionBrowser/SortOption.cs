namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Order in which results will be sorted.
    /// </summary>
    public enum SortOrder
    {
        /// <summary>
        /// Sort results in ascending order.
        /// </summary>
        Ascending,
        /// <summary>
        /// Sort results in descending order.
        /// </summary>
        Descending
    }

    /// <summary>
    /// Supported fields to sort results by.
    /// </summary>
    public enum SortField
    {
        /// <summary>
        /// Sort results by session name.
        /// </summary>
        Name,
        /// <summary>
        /// Sort results by max players.
        /// </summary>
        MaxPlayers,
        /// <summary>
        /// Sort results by available slots.
        /// </summary>
        AvailableSlots,
        /// <summary>
        /// Sort results by creation time.
        /// </summary>
        CreationTime,
        /// <summary>
        /// Sort results by last updated time.
        /// </summary>
        LastUpdated,
        /// <summary>
        /// Sort results by session ID.
        /// </summary>
        Id,
        /// <summary>
        /// Sort results by string index 1.
        /// </summary>
        StringIndex1,
        /// <summary>
        /// Sort results by string index 2.
        /// </summary>
        StringIndex2,
        /// <summary>
        /// Sort results by string index 3.
        /// </summary>
        StringIndex3,
        /// <summary>
        /// Sort results by string index 4.
        /// </summary>
        StringIndex4,
        /// <summary>
        /// Sort results by string index 5.
        /// </summary>
        StringIndex5,
        /// <summary>
        /// Sort results by number index 1.
        /// </summary>
        NumberIndex1,
        /// <summary>
        /// Sort results by number index 2.
        /// </summary>
        NumberIndex2,
        /// <summary>
        /// Sort results by number index 3.
        /// </summary>
        NumberIndex3,
        /// <summary>
        /// Sort results by number index 4.
        /// </summary>
        NumberIndex4,
        /// <summary>
        /// Sort results by number index 5.
        /// </summary>
        NumberIndex5,
    }

    /// <summary>
    /// Options representing how to sort the results of a session query.
    /// </summary>
    public class SortOption
    {
        /// <summary>
        /// Order in which results will be sorted.
        /// </summary>
        public SortOrder Order { get; set; }

        /// <summary>
        /// Field to sort results by.
        /// </summary>
        public SortField Field { get; set; }

        /// <summary>
        /// Construct a sort object with the provided order and field.
        /// </summary>
        /// <param name="order">Order in which to sort results.</param>
        /// <param name="field">Field on which to sort results.</param>
        public SortOption(SortOrder order, SortField field)
        {
            Order = order;
            Field = field;
        }

        /// <summary>
        /// Construct a sort object with default order and field (creation time ascending).
        /// </summary>
        public SortOption()
        {
            Order = SortOrder.Ascending;
            Field = SortField.CreationTime;
        }
    }
}
