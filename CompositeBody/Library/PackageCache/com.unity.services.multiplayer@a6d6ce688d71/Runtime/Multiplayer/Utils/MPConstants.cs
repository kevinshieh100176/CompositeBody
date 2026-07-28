using Unity.Services.Lobbies.Models;

namespace Unity.Services.Multiplayer
{
    class MPConstants
    {
        public const QueryFilter.FieldOptions QueryTypeIndex = QueryFilter.FieldOptions.S1;
        public const DataObject.IndexOptions DataTypeIndex = DataObject.IndexOptions.S1;
        public const QueryFilter.FieldOptions QueryMetadataIndex = QueryFilter.FieldOptions.S2;
        public const DataObject.IndexOptions DataMetadataIndex = DataObject.IndexOptions.S2;
        public const int ConnectionTimeoutSeconds = 10;
    }
}
