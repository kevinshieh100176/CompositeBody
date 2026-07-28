using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FilteredPoolConfig = Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model.FilteredPoolConfig;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.IO
{
    /// <summary>
    /// This converter converts the raw json string for a queue filter value, which can be a string or a number.
    /// </summary>
    class FilterValueConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer,
            object value,
            JsonSerializer serializer)
        {
            if (value is FilteredPoolConfig.Filter.FilterValue filterValue)
            {
                writer.WriteValue(filterValue.Value);
            }
        }

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            string value = string.Empty;
            // because this is a value at this time already, no need to convert it
            if (JToken.ReadFrom(reader) is JValue serializedValue)
            {
                value = serializedValue.Value?.ToString();
            }

            return new FilteredPoolConfig.Filter.FilterValue(value);
        }

        public override bool CanConvert(Type objectType)
        {
            return typeof(FilteredPoolConfig.Filter.FilterValue).IsAssignableFrom(objectType);
        }
    }
}
