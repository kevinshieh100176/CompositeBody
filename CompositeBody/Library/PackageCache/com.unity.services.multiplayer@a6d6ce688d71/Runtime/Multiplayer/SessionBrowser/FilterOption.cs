namespace Unity.Services.Multiplayer
{
    /// <summary>
    /// Supported fields and indices to sort query results by.
    /// </summary>
    public enum FilterField
    {
        /// <summary>
        /// Filter based on the number of maximum number of players in the session.
        /// </summary>
        MaxPlayers,
        /// <summary>
        /// Filter based on the available slots in the session.
        /// </summary>
        AvailableSlots,
        /// <summary>
        /// Filter based on the session name.
        /// </summary>
        Name,
        /// <summary>
        /// Filter based on the created DateTime of the session. Must be in RFC3339 format!
        /// </summary>
        Created,
        /// <summary>
        /// Filter based on the last update DateTime of the session. Must be in RFC3339 format!
        /// </summary>
        LastUpdated,
        /// <summary>
        /// Custom string data field index 1.
        /// </summary>
        StringIndex1,
        /// <summary>
        /// Custom string data field index 2.
        /// </summary>
        StringIndex2,
        /// <summary>
        /// Custom string data field index 3.
        /// </summary>
        StringIndex3,
        /// <summary>
        /// Custom string data field index 4.
        /// </summary>
        StringIndex4,
        /// <summary>
        /// Custom string data field index 5.
        /// </summary>
        StringIndex5,
        /// <summary>
        /// Custom numerical data field index 1.
        /// </summary>
        NumberIndex1,
        /// <summary>
        /// Custom numerical data field index 2.
        /// </summary>
        NumberIndex2,
        /// <summary>
        /// Custom numerical data field index 3.
        /// </summary>
        NumberIndex3,
        /// <summary>
        /// Custom numerical data field index 4.
        /// </summary>
        NumberIndex4,
        /// <summary>
        /// Custom numerical data field index 5.
        /// </summary>
        NumberIndex5,
        /// <summary>
        /// Filter based on the locked state of the sessions.
        /// </summary>
        IsLocked,
        /// <summary>
        /// Filter based on weather the sessions has a password defined.
        /// </summary>
        HasPassword
    }

    /// <summary>
    /// The operation used to compare the field to the filter value.
    /// </summary>
    public enum FilterOperation
    {
        /// <summary>
        /// Contains operation.
        /// </summary>
        Contains,
        /// <summary>
        /// Equal operation.
        /// </summary>
        Equal,
        /// <summary>
        /// Not Equal operation.
        /// </summary>
        NotEqual,
        /// <summary>
        /// Less than operation.
        /// </summary>
        Less,
        /// <summary>
        /// Less than or Equal operation.
        /// </summary>
        LessOrEqual,
        /// <summary>
        /// Greater than operation.
        /// </summary>
        Greater,
        /// <summary>
        /// Greater than or Equal operation.
        /// </summary>
        GreaterOrEqual
    }

    /// <summary>
    /// A Generic class for creating option for querying sessions.
    /// </summary>
    public class FilterOption
    {
        /// <summary>
        /// A filter for an individual field that is applied to a query.
        /// </summary>
        /// <param name="field">The name of the field to filter on. For custom data fields, the name of the index must be used instead of the field name.</param>
        /// <param name="value">The value to compare to the field being filtered.  This value must be a string and it must be parsable as the same type as &#x60;field&#x60; (e.g. &#x60;integer&#x60; for MaxPlayers, &#x60;datetime&#x60; for Created, etc.).  The value for &#x60;datetime&#x60; fields (Created, LastUpdated) must be in RFC3339 format.  For example, in C# this can be achieved using the \&quot;o\&quot; format specifier: &#x60;return dateTime.ToString(\&quot;o\&quot;, DateTimeFormatInfo.InvariantInfo);&#x60;.  Refer to your language documentation for other methods to generate RFC3339-compatible datetime strings.</param>
        /// <param name="operation">The operation used to compare the field to the filter value.</param>
        public FilterOption(FilterField field, string value, FilterOperation operation)
        {
            Field = field;
            Value = value;
            Operation = operation;
        }

        /// <summary>
        /// The name of the field to filter on.  For custom data fields, the name of the index must be used instead of the field name.
        /// </summary>
        public FilterField Field { get; }

        /// <summary>
        /// The value to compare to the field being filtered.  This value must be a string and it must be parsable as the same type as &#x60;field&#x60; (e.g. &#x60;integer&#x60; for MaxPlayers, &#x60;datetime&#x60; for Created, etc.).  The value for &#x60;datetime&#x60; fields (Created, LastUpdated) must be in RFC3339 format.  For example, in C# this can be achieved using the \&quot;o\&quot; format specifier: &#x60;return dateTime.ToString(\&quot;o\&quot;, DateTimeFormatInfo.InvariantInfo);&#x60;.  Refer to your language documentation for other methods to generate RFC3339-compatible datetime strings.
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// The operation used to compare the field to the filter value.  Supports &#x60;CONTAINS&#x60; (only on the &#x60;Name&#x60; field), &#x60;EQ&#x60; (Equal), &#x60;NE&#x60; (Not Equal), &#x60;LT&#x60; (Less Than), &#x60;LE&#x60; (Less Than or Equal), &#x60;GT&#x60; (Greater Than), and &#x60;GE&#x60; (Greater Than or Equal).
        /// </summary>
        public FilterOperation Operation { get; }
    }
}
