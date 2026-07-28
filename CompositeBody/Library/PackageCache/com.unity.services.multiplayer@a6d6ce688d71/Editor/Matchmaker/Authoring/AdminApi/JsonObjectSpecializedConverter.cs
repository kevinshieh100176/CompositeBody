using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.AdminApi
{
    /// <summary>
    /// This converter stores the raw json string for complex object that we don't care to deserialize and the client generator can't handle
    /// It can take a json object, array, string or number literal and output it back without messing the type
    /// </summary>
    class JsonObjectSpecializedConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value is JsonObject valueJson)
            {
                if (string.IsNullOrWhiteSpace(valueJson.Value))
                {
                    writer.WriteNull();
                }
                else
                {
                    var obj = JToken.Parse(valueJson.Value);
                    obj.WriteTo(writer);
                }
            }
        }

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            var obj = JToken.ReadFrom(reader);
            if (obj.Type == JTokenType.String)
                return new MatchmakerAdminClient.JsonObjectSpecialized("\"" + obj + "\"");
            return new MatchmakerAdminClient.JsonObjectSpecialized(obj.ToString());
        }

        public override bool CanConvert(Type objectType)
        {
            return typeof(JsonObject).IsAssignableFrom(objectType);
        }
    }
}
