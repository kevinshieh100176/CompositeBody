using System;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model
{
    [Serializable]
    class JsonObject
    {
        public JsonObject(string value)
        {
            Value = value;
        }

        public string Value { get; set; }
    }
}
