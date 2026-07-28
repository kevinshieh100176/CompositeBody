namespace Unity.Services.Vivox
{
    internal interface IAccessTokenServer
    {
        string Issuer { get; }

        string Key { get; }

        System.TimeSpan ExpirationTimeSpan { get; }
    }
}
