using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// The unique identifier for a player that accesses a Vivox instance.
    /// </summary>
    internal class AccountId
    {
        public const int _accountNameMaxLength = 127;

        private readonly string _domain;
        private readonly string _displayname;
        private readonly string[] _spokenLanguages;

        /// <summary>
        /// Create an AccountId from a URI - Internal Use Only.
        /// </summary>
        /// <param name="uri">The URI of the account.</param>
        /// <param name="displayname">Optional: The display name for an account. This string must not exceed a 63 byte length when encoded in UTF-8.</param>
        public AccountId(string uri, string displayname = null)
        {
            if (string.IsNullOrEmpty(uri))
            {
                return;
            }

            var matchGroups = new Regex(@"sip:\.(?<issuer>[^.]+)\.(?<playerId>.*?)(?:\.(?<envId>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}))?\.@(?<domain>[a-zA-Z0-9.]+)").Match(uri);

            if (matchGroups == null || !matchGroups.Success || matchGroups.Groups.Count < 3)
            {
                throw new ArgumentException($"'{uri}' is not a valid URI");
            }

            Issuer = matchGroups.Groups["issuer"].Value;
            Name = matchGroups.Groups["playerId"].Value;
            EnvironmentId = matchGroups.Groups["envId"].Value;
            _domain = string.IsNullOrEmpty(Client.defaultRealm) ? matchGroups.Groups["domain"].Value : Client.defaultRealm;

            if (!IsValidName(Name))
            {
                throw new ArgumentException("Argument is empty or contains one, or more, invalid characters.", "name");
            }

            var accountName = string.IsNullOrEmpty(EnvironmentId) ? $".{Issuer}.{Name}." : $".{Issuer}.{Name}.{EnvironmentId}.";
            if (accountName.Length > _accountNameMaxLength)
            {
                throw new ArgumentException(GetType().Name + ": The string \".{issuer}.{name}.\" must have a length of no greater than " + _accountNameMaxLength + " characters.");
            }

            if (!string.IsNullOrEmpty(displayname))
            {
                byte[] displaynameBytes = Encoding.UTF8.GetBytes(displayname);
                if (displaynameBytes.Length > _accountNameMaxLength)
                    throw new ArgumentException($"{GetType().Name}: The string displayname must have a length of no greater than {_accountNameMaxLength} characters.");
            }

            _displayname = displayname;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="issuer">The issuer that is responsible for authenticating this account.</param>
        /// <param name="name">The name you assign to the player.</param>
        /// <param name="domain">The Vivox domain that provides service for this account. For example: vfd.vivox.com.</param>
        /// <param name="displayname">Optional: The display name for an account. This string must not exceed a 63 byte length when encoded in UTF-8.</param>
        /// <param name="spokenLanguages">An optional array of languages used as hints for audio transcription. The default is an empty array, which implies "en".</param>
        /// <remarks>
        /// You can specify up to three spoken languages in order of preference to inform transcription of all users in transcribed channels.
        /// IETF language tag strings are not validated, but are expected to conform to <a href="https://tools.ietf.org/html/bcp47">BCP 47</a>.
        /// The total length of '.{issuer}.{name}.' must be less than 63 characters, and can only use the letters a-z, the numbers 0-9, and the special characters: =+-_.!~()%
        /// </remarks>
        public AccountId(string issuer, string name, string domain, string displayname = null, string[] spokenLanguages = null, string environmentId = null)
        {
            if (string.IsNullOrEmpty(issuer))
                throw new ArgumentNullException(nameof(issuer));
            if (!IsValidName(name))
                throw new ArgumentException($"{GetType().Name}: Argument is empty or contains one, or more, invalid characters.", nameof(name));
            if (string.IsNullOrEmpty(domain))
                throw new ArgumentNullException(nameof(domain));
            // The environment and player IDs are not required but we should always have them in tandem when we do have them.
            // If we have them we will plug both into the AccountId URI
            if (!string.IsNullOrEmpty(environmentId))
                EnvironmentId = environmentId;
            if (domain.IndexOf(".", StringComparison.Ordinal) <= 0)
                throw new ArgumentException($"{GetType().Name}: Argument does not look like a domain. It should be in the form like vd1.vivox.com (currently missing '.')", nameof(domain));


            var accountName = string.IsNullOrEmpty(environmentId) ? $".{issuer}.{name}." : $".{issuer}.{name}.{environmentId}.";
            if (accountName.Length > _accountNameMaxLength)
            {
                throw new ArgumentException(GetType().Name + ": The string \".{issuer}.{name}.\" must have a length of no greater than " + _accountNameMaxLength + " characters.");
            }

            if (!string.IsNullOrEmpty(displayname))
            {
                byte[] displaynameBytes = Encoding.UTF8.GetBytes(displayname);
                if (displaynameBytes.Length > _accountNameMaxLength)
                    throw new ArgumentException($"{GetType().Name}: The string displayname must have a length of no greater than {_accountNameMaxLength} characters.");
            }
            if (spokenLanguages != null)
            {
                for (int i = 0; i < spokenLanguages.Length; ++i)
                {
                    spokenLanguages[i] = spokenLanguages[i].Replace(" ", "");
                }
            }

            Issuer = issuer;
            Name = name;
            _domain = string.IsNullOrEmpty(Client.defaultRealm) ? domain : Client.defaultRealm;
            _displayname = displayname;
            _spokenLanguages = spokenLanguages;
        }

        /// <summary>
        /// The issuer assigned to your game.
        /// </summary>
        public string Issuer { get; }
        /// <summary>
        /// The name you assign to the player.
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// The UAS Environment ID of account.
        /// </summary>
        public string EnvironmentId { get; }
        /// <summary>
        /// This is a value that your developer support representative provides. It is subject to change if a different server-determined destination is provided during client connector creation.
        /// </summary>
        public string Domain => string.IsNullOrEmpty(Client.defaultRealm) ? _domain : Client.defaultRealm;
        /// <summary>
        /// An optional array of languages used as hints for audio transcription. The default is an empty array, which implies "en".
        /// You can specify up to three spoken languages in order of preference to inform transcription of all users in transcribed channels.
        /// IETF language tag strings are not validated, but are expected to conform to <a href="https://tools.ietf.org/html/bcp47">BCP 47</a>.
        /// </summary>
        public string[] SpokenLanguages => _spokenLanguages ?? new string[0];

        internal string AccountName
        {
            get
            {
                if (IsEmpty) return "";
                // If we have the Unity Game Service authentication values we use the playerId in place of the name.
                //return $".{_issuer}.{_name}.";
                return string.IsNullOrEmpty(EnvironmentId) ? $".{Issuer}.{Name}." : $".{Issuer}.{Name}.{EnvironmentId}.";
            }
        }
        /// <summary>
        /// The name you assign to the player for display purposes. If a name is not set, then Name is returned.
        /// If a display name is not set, return Name.
        /// </summary>
        public string DisplayName => string.IsNullOrEmpty(_displayname) ? Name : _displayname;
        /// <summary>
        /// Determine if two objects are equal.
        /// </summary>
        /// <param name="obj">The other object.</param>
        /// <returns>True if the objects are of equal value.</returns>
        public override bool Equals(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            if (GetType() != obj.GetType())
            {
                return false;
            }

            return Equals((AccountId)obj);
        }

        private bool Equals(AccountId other)
        {
            return string.Equals(_domain, other._domain) && string.Equals(Name, other.Name) &&
                string.Equals(Issuer, other.Issuer);
        }

        /// <summary>
        /// A hash function for AccountId.
        /// </summary>
        /// <returns>A hash code for the current object.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                var hc = (_domain?.GetHashCode() ?? 0);
                hc *= 397;
                hc ^= (Name?.GetHashCode() ?? 0);
                hc *= 397;
                hc ^= (Issuer?.GetHashCode() ?? 0);
                return hc;
            }
        }

        /// <summary>
        /// This is true if the Name, Domain, and Issuer are empty.
        /// </summary>
        public bool IsEmpty => string.IsNullOrEmpty(Name) && string.IsNullOrEmpty(_domain) && string.IsNullOrEmpty(Issuer);

        /// <summary>
        /// A test for an empty AccountId.
        /// </summary>
        /// <param name="id">The account ID.</param>
        /// <returns>True if the ID is null or empty.</returns>
        public static bool IsNullOrEmpty(AccountId id)
        {
            return id == null || id.IsEmpty;
        }

        /// <summary>
        /// The network representation of the account ID.
        /// </summary>
        /// <returns>A URI for this account.</returns>
        /// <remarks>
        /// Note: This will be refactored in the future so the internal network representation of the AccountId is hidden.
        /// </remarks>
        public override string ToString()
        {
            if (IsEmpty) return "";
            return string.IsNullOrEmpty(EnvironmentId) ? $"sip:.{Issuer}.{Name}.@{_domain}" : $"sip:.{Issuer}.{Name}.{EnvironmentId}.@{_domain}";
        }

        /// <summary>
        /// Check each character within the "name" argument against a collection of valid characters that coincide with Vivox naming conventions.
        /// </summary>
        /// <returns>If the name is valid.</returns>
        internal bool IsValidName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            const string validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890=+-_.!~()%";
            foreach (char c in name.ToCharArray())
            {
                if (!validChars.Contains(c.ToString()))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
