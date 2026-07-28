namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// The name of the column to index this property value under, either &#x60;S#&#x60; for strings or &#x60;N#&#x60; for numeric values.  If an index is specified on a property, then you can use that index name in a &#x60;QueryFilter&#x60; to filter results by that property.  You will not be prevented from indexing multiple objects having properties with different names but the same index, but you will likely receive unexpected results from a query.
    /// </summary>
    /// <value>The name of the column to index this property value under, either &#x60;S#&#x60; for strings or &#x60;N#&#x60; for numeric values.  If an index is specified on a property, then you can use that index name in a &#x60;QueryFilter&#x60; to filter results by that property.  You will not be prevented from indexing multiple objects having properties with different names but the same index, but you will likely receive unexpected results from a query.</value>
    public enum PropertyIndex
    {
        /// <summary>
        /// None.
        /// </summary>
        None    = 0,
        /// <summary>
        /// String index 1.
        /// </summary>
        String1 = 1,
        /// <summary>
        /// String index 2.
        /// </summary>
        String2 = 2,
        /// <summary>
        /// String index 3.
        /// </summary>
        String3 = 3,
        /// <summary>
        /// String index 4.
        /// </summary>
        String4 = 4,
        /// <summary>
        /// String index 5.
        /// </summary>
        String5 = 5,
        /// <summary>
        /// Number index 1.
        /// </summary>
        Number1 = 6,
        /// <summary>
        /// Number index 2.
        /// </summary>
        Number2 = 7,
        /// <summary>
        /// Number index 3.
        /// </summary>
        Number3 = 8,
        /// <summary>
        /// Number index 4.
        /// </summary>
        Number4 = 9,
        /// <summary>
        /// Number index 5.
        /// </summary>
        Number5 = 10
    }
}
