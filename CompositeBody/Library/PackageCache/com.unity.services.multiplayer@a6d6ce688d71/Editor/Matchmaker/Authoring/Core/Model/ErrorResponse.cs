using System.Runtime.Serialization;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.Model
{
    class ErrorResponse
    {
        [DataMember(IsRequired = true)] public string ResultCode  {get; set;}

        [DataMember(IsRequired = true)] public string Message { get; set; }
    }
}
