using Unity.Services.Authentication.Internal;
using UnityEngine;

namespace Unity.Services.Multiplayer
{
    interface IServiceID
    {
        string ServiceID { get; }
    }

    class InternalServiceID : IServiceID
    {
        public string ServiceID => GetServiceID();

        readonly IAccessToken m_AccessToken;
        string m_ServiceID;

        public InternalServiceID(IAccessToken accessToken)
        {
            m_AccessToken = accessToken;
        }

        string GetServiceID()
        {
            if (m_AccessToken?.AccessToken == null)
            {
                Logger.LogError("error: cannot get ServiceID until the server is authenticated");
                return null;
            }

            if (string.IsNullOrEmpty(m_ServiceID))
            {
                // note(luke): this is a duplication of the auth service internal logic to decode the JWT to retrieve
                // the server token subject. We should make this a first class property of the IServerAccessToken
                // similar to PlayerID.
                m_ServiceID = new JwtDecoder(new DateTimeWrapper()).Decode<ServerAccessToken>(m_AccessToken.AccessToken).Subject;
            }

            return m_ServiceID;
        }
    }
}
