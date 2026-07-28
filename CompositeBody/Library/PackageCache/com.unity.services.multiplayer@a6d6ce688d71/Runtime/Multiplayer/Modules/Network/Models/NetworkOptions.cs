using System;
using Unity.Services.Relay.Models;

namespace Unity.Services.Multiplayer
{
    class NetworkModuleOptions : IModuleOption
    {
        public Type Type => typeof(NetworkModuleOptions);

        NetworkOptions Options { get; }

        public NetworkModuleOptions(NetworkOptions options)
        {
            Options = options;
        }

        public void Process(SessionHandler session)
        {
            var module = session.GetModule<NetworkModule>();

            if (module == null)
            {
                throw new Exception("Trying to setup network in session but the module isn't registered.");
            }

            // Making sure a valid protocol is passed to the module.
            Options.RelayProtocol = GetValidProtocol(Options.RelayProtocol);
            static RelayProtocol GetValidProtocol(RelayProtocol protocol)
            {
                if (AllocationUtils.IsValidProtocol(protocol))
                {
                    return protocol;
                }

                Logger.LogWarning($"Invalid protocol \"{protocol:G}\" detected. Using default protocol \"{RelayProtocol.Default:G}\" instead.");

                return RelayProtocol.Default;
            }

            module.NetworkOptions = Options;
        }
    }

    /// <summary>
    /// Represents network specific options that can be configured when creating or joining a <see cref="ISession"/>.
    /// </summary>
    /// <remarks>
    /// Use the <see cref="SessionOptionsExtensions.WithNetworkOptions{T}"/> when creating
    /// or joining a <see cref="ISession"/> to configure your network setup.
    /// </remarks>
    public class NetworkOptions
    {
        /// <summary>
        /// Overrides the <see cref="RelayProtocol"/> that the client will use when communicating with the Relay server.
        /// </summary>
        /// <remarks>
        /// This value is only used when connecting to a <see cref="ISession"/>
        /// that is configured with one of these Network connection:<br />
        /// <see cref="SessionOptionsExtensions.WithRelayNetwork{T}(T, string)"/><br/>
        /// <see cref="SessionOptionsExtensions.WithRelayNetwork{T}(T, RelayNetworkOptions)"/><br/>
        /// <see cref="SessionOptionsExtensions.WithDistributedAuthorityNetwork{T}(T, string)"/><br/>
        /// <see cref="SessionOptionsExtensions.WithDistributedAuthorityNetwork{T}(T, RelayNetworkOptions)"/><br/>
        /// </remarks>
        public RelayProtocol RelayProtocol { get; set; } = RelayProtocol.Default;
    }
}
