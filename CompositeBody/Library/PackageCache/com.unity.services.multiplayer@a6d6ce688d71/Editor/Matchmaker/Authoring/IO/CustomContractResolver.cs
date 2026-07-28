using System.Reflection;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.IO
{
    class CustomContractResolver : CamelCasePropertyNamesContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            JsonProperty property = base.CreateProperty(member, memberSerialization);

            // Check if a DataMember attribute is present
            var dataMemberAttribute = member.GetCustomAttribute<DataMemberAttribute>();
            if (dataMemberAttribute != null)
            {
                // If a Name has been provided, use it
                if (!string.IsNullOrEmpty(dataMemberAttribute.Name))
                {
                    property.PropertyName = dataMemberAttribute.Name;
                }

                if (dataMemberAttribute.IsRequired)
                {
                    property.Required = Required.Always;
                }
            }
            return property;
        }
    }
}
