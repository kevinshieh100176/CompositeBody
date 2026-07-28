using System;
using System.Text.RegularExpressions;

namespace Unity.Services.Vivox
{
    /// <summary>
    /// The unique identifier for a channel. Channels are created and destroyed automatically on demand.
    /// </summary>
    internal class ChannelId
    {
        internal string GetUriDesignator(ChannelType value)
        {
            switch (value)
            {
                case ChannelType.Echo:
                    return "e";
                case ChannelType.NonPositional:
                    return "g";
                case ChannelType.Positional:
                    return "d";
            }
            throw new ArgumentException($"{GetType().Name}: {value} has no GetUriDesignator() support");
        }

        private readonly string _domain;
        private readonly string _nameWithoutLargeText = null; // name with marker removed

        public ChannelId(string uri)
        {
            if (string.IsNullOrEmpty(uri))
                return;

            // Anchor overall URI to extract designator, issuer, tail (name + optional env + optional props), and domain.
            var topPattern = @"^sip:confctl-(?<uriDesignator>e|g|d)-(?<issuer>[^.]+)\.(?<tail>[^@]+)@(?<domain>[a-zA-Z0-9.]+)$";
            var topMatch = new Regex(topPattern).Match(uri);
            if (topMatch == null || !topMatch.Success)
            {
                throw new ArgumentException($"'{uri}' is not a valid URI");
            }

            var type = topMatch.Groups["uriDesignator"].Value;
            switch (type)
            {
                case "g":
                    Type = ChannelType.NonPositional;
                    break;
                case "e":
                    Type = ChannelType.Echo;
                    break;
                case "d":
                    Type = ChannelType.Positional;
                    break;
                default:
                    throw new ArgumentException($"{GetType().Name}: {uri} is not a valid URI");
            }

            Issuer = topMatch.Groups["issuer"].Value;

            // The tail contains: <name> | <name>.<envId> | <name>!p-<props> | <name>.<envId>!p-<props>
            var tail = topMatch.Groups["tail"].Value ?? string.Empty;
            string envId = null;
            string props = string.Empty;
            string namePart = tail;

            // GUID regex fragment
            var guidPattern = "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}";

            if (Type == ChannelType.Positional)
            {
                // Try to extract an environment GUID at the end (and optional props after it)
                var envPattern = $@"^(?<name>.*)\.(?<env>{guidPattern})(?:!p-(?<props>.*))?$";
                var envMatch = new Regex(envPattern).Match(tail);
                if (envMatch != null && envMatch.Success)
                {
                    namePart = envMatch.Groups["name"].Value;
                    envId = envMatch.Groups["env"].Value;
                    if (envMatch.Groups["props"].Success)
                        props = envMatch.Groups["props"].Value;
                }
                else
                {
                    // No env GUID found; check for positional props marker at the very end.
                    // Treat '!p-' as a props marker if it's present anywhere after the name portion; use LastIndexOf so occurrences of "!p-" inside the name are preserved.
                    var idx = tail.LastIndexOf("!p-", StringComparison.Ordinal);
                    if (idx >= 0 && idx + 3 <= tail.Length)
                    {
                        namePart = tail.Substring(0, idx);
                        props = tail.Substring(idx + 3);
                    }
                    else
                    {
                        namePart = tail;
                    }
                }
            }
            else
            {
                // Non-positional or echo channels: only extract trailing env GUID if present; do not treat '!p-' as props marker to preserve names containing '!p-'.
                var envPattern = $@"^(?<name>.*)\.(?<env>{guidPattern})$";
                var envMatch = new Regex(envPattern).Match(tail);
                if (envMatch != null && envMatch.Success)
                {
                    namePart = envMatch.Groups["name"].Value;
                    envId = envMatch.Groups["env"].Value;
                }
                else
                {
                    namePart = tail;
                }
            }

            // parse large-text hunk from channelName
            var parsed = ParseLargeTextMarker(namePart ?? string.Empty);
            RawName = parsed.rawName;
            _nameWithoutLargeText = parsed.strippedName;
            IsLargeText = parsed.isLarge;

            EnvironmentId = envId ?? string.Empty;
            if (Type == ChannelType.Positional)
            {
                Properties = new Channel3DProperties(props);
            }

            _domain = string.IsNullOrEmpty(Client.defaultRealm) ? topMatch.Groups["domain"].Value : Client.defaultRealm;
        }

        /// <summary>
        /// A constructor for creating an echo or non-positional channel.
        /// </summary>
        /// <param name="issuer">The issuer that is responsible for authorizing access to this channel.</param>
        /// <param name="name">The name of this channel.</param>
        /// <param name="domain">The Vivox domain that hosts this channel.</param>
        /// <param name="type">The channel type.</param>
        /// <param name="properties">The 3D positional channel properties.</param>
        /// <param name="environmentId">Environment ID for Unity Game Service-specific integrations </param>
        /// <param name="isLargeText">If true, treat the channel as large text and append the (t-largetext) hunk when serializing if it's not already present.</param>
        public ChannelId(string issuer, string name, string domain, ChannelType type = ChannelType.NonPositional, Channel3DProperties properties = null, string environmentId = null, bool isLargeText = false)
        {
            if (string.IsNullOrEmpty(issuer)) throw new ArgumentNullException(nameof(issuer));
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (string.IsNullOrEmpty(domain)) throw new ArgumentNullException(nameof(domain));
            // EnvironmentId is not required but if we have it we treat it as a separate section in the URI and need to surround it with dots.
            if (!string.IsNullOrEmpty(environmentId)) EnvironmentId = environmentId ?? string.Empty;
            if (!Enum.IsDefined(typeof(ChannelType), type)) throw new ArgumentOutOfRangeException(type.ToString());
            if (properties == null) Properties = new Channel3DProperties();
            else if (type == ChannelType.Positional && !properties.IsValid()) throw new ArgumentException("", nameof(properties));
            else Properties = properties;

            // If caller explicitly requested large text, ensure RawName contains the hunk; otherwise, parse name to detect it.
            if (isLargeText)
            {
                // Use ParseLargeTextMarker to avoid duplicating hunk if it's already present.
                var parsed = ParseLargeTextMarker(name);
                if (parsed.isLarge)
                {
                    RawName = parsed.rawName;
                    _nameWithoutLargeText = parsed.strippedName;
                    IsLargeText = true;
                }
                else
                {
                    RawName = name + "(t-largetext)";
                    _nameWithoutLargeText = name;
                    IsLargeText = true;
                }
            }
            else
            {
                var parsed = ParseLargeTextMarker(name);
                RawName = parsed.rawName;
                _nameWithoutLargeText = parsed.strippedName;
                IsLargeText = parsed.isLarge;
            }

            if (!IsValidName(_nameWithoutLargeText))
            {
                throw new ArgumentException($"{GetType().Name}: Argument contains one, or more, invalid characters, or the length of the name exceeds 200 characters.", nameof(name));
            }

            Issuer = issuer;
            _domain = string.IsNullOrEmpty(Client.defaultRealm) ? domain : Client.defaultRealm;
            Type = type;
        }

        /// <summary>
        /// The issuer that is responsible for authorizing access to this channel.
        /// </summary>
        public string Issuer { get; }
        /// <summary>
        /// The name of this channel. NOTE: this returns the name with the "(t-...)" large-text hunk removed when present.
        /// </summary>
        public string Name => _nameWithoutLargeText ?? RawName;

        /// <summary>
        /// The original raw channel name as parsed from the URI or provided to the constructor.
        /// </summary>
        public string RawName { get; } = string.Empty;

        /// <summary>
        /// True if the channel name contained a trailing "(t-...)" marker indicating a large-text channel.
        /// </summary>
        public bool IsLargeText { get; } = false;

        /// <summary>
        /// The UAS Environment ID of this channel.
        /// </summary>
        public string EnvironmentId { get; }
        /// <summary>
        /// This is a value that your developer support representative provides. It is subject to change if a different server-determined destination is provided during client connector creation.
        /// </summary>
        public string Domain => string.IsNullOrEmpty(Client.defaultRealm) ? _domain : Client.defaultRealm;
        /// <summary>
        /// The channel type.
        /// </summary>
        public ChannelType Type { get; }
        /// <summary>
        /// The 3D channel properties.
        /// </summary>
        public Channel3DProperties Properties { get; }

        /// <summary>
        /// Determine if two objects are equal.
        /// </summary>
        /// <param name="obj">The other object.</param>
        /// <returns>True if the objects are of equal value.</returns>
        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;
            if (GetType() != obj.GetType()) return false;

            return Equals((ChannelId)obj);
        }

        bool Equals(ChannelId other)
        {
            return string.Equals(_domain, other._domain) && string.Equals(Name, other.Name) &&
                string.Equals(Issuer, other.Issuer) && Type == other.Type;
        }

        /// <summary>
        /// A hash function for ChannelId.
        /// </summary>
        /// <returns>A hash code for the current object.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                var hc = _domain?.GetHashCode() ?? 0;
                hc = (hc * 397) ^ (Name?.GetHashCode() ?? 0);
                hc = (hc * 397) ^ (Issuer?.GetHashCode() ?? 0);
                hc = (hc * 397) ^ (_domain?.GetHashCode() ?? 0);
                hc = (hc * 397) ^ Type.GetHashCode();
                return hc;
            }
        }

        /// <summary>
        /// This is true if the Name, Domain, and Issuer are empty.
        /// </summary>
        public bool IsEmpty => string.IsNullOrEmpty(Name) && string.IsNullOrEmpty(_domain) && string.IsNullOrEmpty(Issuer);
        /// <summary>
        /// A test for an empty ChannelId.
        /// </summary>
        /// <param name="id">The channel ID.</param>
        /// <returns>True if the ID is null or empty.</returns>
        public static bool IsNullOrEmpty(ChannelId id)
        {
            return id == null || id.IsEmpty;
        }

        /// <summary>
        /// The network representation of the channel ID.
        /// Note: This will be refactored in the future so the internal network representation of the ChannelId is hidden.
        /// </summary>
        /// <returns>A URI for this channel.</returns>
        public override string ToString()
        {
            if (!IsValid()) return "";

            // Use the raw name when serializing to preserve the original marker in the URI (if present).
            string props = Type == ChannelType.Positional ? Properties.ToString() : string.Empty;
            var uri = string.IsNullOrEmpty(EnvironmentId)
                ? $"sip:confctl-{GetUriDesignator(Type)}-{Issuer}.{RawName}{props}@{Domain}"
                : $"sip:confctl-{GetUriDesignator(Type)}-{Issuer}.{RawName}.{EnvironmentId}{props}@{Domain}";
            return uri;
        }

        /// <summary>
        /// Ensure that _name, _domain, and _issuer are not empty, and further validate the _name field by checking it against an array of valid characters.
        /// Note: If the channel is positional, then _properties is also validated.
        /// </summary>
        /// <returns>If the ChannelID is valid.</returns>
        internal bool IsValid()
        {
            if (IsEmpty || !IsValidName(Name) || (Type == ChannelType.Positional && !Properties.IsValid()))
                return false;
            return true;
        }

        /// <summary>
        /// Check the name value against a max length and group of valid characters.
        /// </summary>
        /// <returns>If the name is valid.</returns>
        internal bool IsValidName(string name)
        {
            if (name.Length > 200)
                return false;

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

        // Helper used only from constructors to avoid assigning readonly fields outside of constructors.
        private static (bool isLarge, string rawName, string strippedName) ParseLargeTextMarker(string name)
        {
            var raw = name ?? string.Empty;
            if (string.IsNullOrEmpty(raw))
                return (false, raw, raw);

            // Match a trailing "(t-...)", capturing the marker without parentheses
            var m = Regex.Match(raw, @"\(t-[^)]+\)$");
            if (m.Success)
            {
                var stripped = raw.Substring(0, m.Index);
                return (true, raw, stripped);
            }

            return (false, raw, raw);
        }
    }
}
