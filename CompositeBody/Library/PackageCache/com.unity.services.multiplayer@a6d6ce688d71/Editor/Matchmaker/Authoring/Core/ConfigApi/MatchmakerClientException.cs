using System;

namespace Unity.Services.Multiplayer.Editor.Matchmaker.Authoring.Core.ConfigApi
{
    class MatchmakerClientException : Exception
    {
        public int StatusCode { get; set; }
        public string ErrorType { get; set; }
        public MatchmakerClientException(string message, Exception e = null)
            : base(message, e) {}
    }
}
