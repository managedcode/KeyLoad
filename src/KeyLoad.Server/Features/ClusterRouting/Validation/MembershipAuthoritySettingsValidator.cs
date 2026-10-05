using System.Security.Cryptography;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class MembershipAuthoritySettingsValidator
{

    internal static void Validate(MembershipAuthoritySettings settings, NodeOptions node)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(node);
        if (settings.Mode == MembershipAuthoritySettingsProtocol.Local)
        {
            if (HasAuthorityFields(settings) || HasTrustedFields(settings))
            { throw new InvalidOperationException(MembershipAuthoritySettingsProtocol.Invalid); }
            return;
        }
        ValidateTwoGroupNode(node);
        if (settings.Mode == MembershipAuthoritySettingsProtocol.Proxy)
        { ValidateProxy(settings, node); return; }
        if (settings.Mode == MembershipAuthoritySettingsProtocol.Authority)
        { ValidateAuthority(settings, node); return; }
        throw new InvalidOperationException(MembershipAuthoritySettingsProtocol.Invalid);
    }

    internal static void ValidateSection(IConfigurationSection section, MembershipAuthoritySettings settings)
    {
        ArgumentNullException.ThrowIfNull(section);
        var children = section.GetChildren().ToArray();
        var allowed = settings.Mode switch
        {
            MembershipAuthoritySettingsProtocol.Local => new[] { "Mode" },
            MembershipAuthoritySettingsProtocol.Authority => new[]
            { "Mode", "TrustedGroupPhysicalShardId", "TrustedGroupIncarnation", "TrustedGroupVoterIds", "TrustedGroupSiloEndpoints", "TrustedGroupPeerSecret" },
            MembershipAuthoritySettingsProtocol.Proxy => new[]
            { "Mode", "AuthorityPhysicalShardId", "AuthorityIncarnation", "AuthorityEndpoints", "AuthorityPeerSecret" },
            _ => []
        };
        if (children.Any(child => !allowed.Contains(child.Key, StringComparer.Ordinal))
            || children.Any(child => child.Value is not null && child.GetChildren().Any()))
        { throw new InvalidOperationException(MembershipAuthoritySettingsProtocol.Invalid); }
        ValidateArray(section, "AuthorityEndpoints", settings.Mode == MembershipAuthoritySettingsProtocol.Proxy);
        ValidateArray(section, "TrustedGroupVoterIds", settings.Mode == MembershipAuthoritySettingsProtocol.Authority);
        ValidateArray(section, "TrustedGroupSiloEndpoints", settings.Mode == MembershipAuthoritySettingsProtocol.Authority);
    }

    private static void ValidateProxy(MembershipAuthoritySettings settings, NodeOptions node)
    {
        if (settings.AuthorityPhysicalShardId == Guid.Empty || settings.AuthorityIncarnation == Guid.Empty
            || settings.AuthorityPhysicalShardId == node.PhysicalShardId
            || settings.AuthorityIncarnation == node.Incarnation
            || settings.AuthorityEndpoints.Length != MembershipAuthoritySettingsProtocol.RequiredMembers
            || settings.AuthorityEndpoints.Distinct(StringComparer.Ordinal).Count() != MembershipAuthoritySettingsProtocol.RequiredMembers
            || settings.AuthorityEndpoints.Any(endpoint => !ValidOrigin(endpoint))
            || !ValidSecret(settings.AuthorityPeerSecret)
            || string.Equals(settings.AuthorityPeerSecret, node.PeerSecret, StringComparison.Ordinal)
            || settings.AuthorityEndpoints.Intersect(node.Peers, StringComparer.Ordinal).Any()
            || HasTrustedFields(settings))
        { throw new InvalidOperationException(MembershipAuthoritySettingsProtocol.Invalid); }
    }

    private static void ValidateAuthority(MembershipAuthoritySettings settings, NodeOptions node)
    {
        if (settings.TrustedGroupPhysicalShardId == Guid.Empty || settings.TrustedGroupIncarnation == Guid.Empty
            || settings.TrustedGroupPhysicalShardId == node.PhysicalShardId
            || settings.TrustedGroupIncarnation == node.Incarnation
            || settings.TrustedGroupVoterIds.Length != MembershipAuthoritySettingsProtocol.RequiredMembers
            || settings.TrustedGroupVoterIds.Distinct(StringComparer.Ordinal).Count() != MembershipAuthoritySettingsProtocol.RequiredMembers
            || settings.TrustedGroupVoterIds.Any(id => !ValidOrigin(id))
            || settings.TrustedGroupSiloEndpoints.Length != MembershipAuthoritySettingsProtocol.RequiredMembers
            || settings.TrustedGroupSiloEndpoints.Distinct(StringComparer.Ordinal).Count() != MembershipAuthoritySettingsProtocol.RequiredMembers
            || settings.TrustedGroupSiloEndpoints.Any(endpoint => !ValidSiloEndpoint(endpoint))
            || !ValidSecret(settings.TrustedGroupPeerSecret)
            || string.Equals(settings.TrustedGroupPeerSecret, node.PeerSecret, StringComparison.Ordinal)
            || settings.TrustedGroupVoterIds.Intersect(node.Peers, StringComparer.Ordinal).Any()
            || HasAuthorityFields(settings))
        { throw new InvalidOperationException(MembershipAuthoritySettingsProtocol.Invalid); }
        for (var index = 0; index < MembershipAuthoritySettingsProtocol.RequiredMembers; index++)
        {
            if (!string.Equals(new Uri(settings.TrustedGroupVoterIds[index]).DnsSafeHost,
                    SiloHost(settings.TrustedGroupSiloEndpoints[index]), StringComparison.Ordinal))
            { throw new InvalidOperationException(MembershipAuthoritySettingsProtocol.Invalid); }
        }
    }

    private static void ValidateTwoGroupNode(NodeOptions node)
    {
        if (node.BenchmarkTopology || !node.AllowPrivateNetworkHttp || node.Peers.Count != MembershipAuthoritySettingsProtocol.RequiredMembers
            || string.IsNullOrWhiteSpace(node.ClusterId) || node.PhysicalShardId == Guid.Empty || node.Incarnation == Guid.Empty)
        { throw new InvalidOperationException(MembershipAuthoritySettingsProtocol.Invalid); }
    }

    private static bool ValidOrigin(string value)
        => value is { Length: > 0 and <= MembershipAuthoritySettingsProtocol.MaximumIdentityBytes }
            && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment) && uri.AbsolutePath == "/";

    private static bool ValidSiloEndpoint(string value)
    {
        if (value is not { Length: > 0 and <= MembershipAuthoritySettingsProtocol.MaximumIdentityBytes })
        { return false; }
        var separator = value.LastIndexOf(':');
        return separator > 0 && int.TryParse(value[(separator + 1)..], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var port)
            && port == MembershipAuthoritySettingsProtocol.NativeSiloPort
            && string.Equals(value, value.Trim(), StringComparison.Ordinal)
            && value[..separator].All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-');
    }

    private static string SiloHost(string value) => value[..value.LastIndexOf(':')];

    private static bool ValidSecret(string? value)
    {
        Span<byte> bytes = stackalloc byte[MembershipAuthoritySettingsProtocol.SecretBytes];
        try
        {
            return value is { Length: 44 } && Convert.TryFromBase64String(value, bytes, out var count)
                && count == bytes.Length && string.Equals(Convert.ToBase64String(bytes), value, StringComparison.Ordinal);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static bool HasAuthorityFields(MembershipAuthoritySettings settings)
        => settings.AuthorityPhysicalShardId != Guid.Empty || settings.AuthorityIncarnation != Guid.Empty
            || settings.AuthorityEndpoints.Length != 0 || settings.AuthorityPeerSecret is not null;

    private static bool HasTrustedFields(MembershipAuthoritySettings settings)
        => settings.TrustedGroupPhysicalShardId != Guid.Empty || settings.TrustedGroupIncarnation != Guid.Empty
            || settings.TrustedGroupVoterIds.Length != 0 || settings.TrustedGroupSiloEndpoints.Length != 0
            || settings.TrustedGroupPeerSecret is not null;

    private static void ValidateArray(IConfigurationSection section, string name, bool required)
    {
        var child = section.GetSection(name);
        var values = child.GetChildren().Take(MembershipAuthoritySettingsProtocol.RequiredMembers + 1).ToArray();
        if (child.Value is not null || required && values.Length != MembershipAuthoritySettingsProtocol.RequiredMembers
            || !required && values.Length != 0
            || required && !values.Select(item => item.Key).SequenceEqual(["0", "1", "2"], StringComparer.Ordinal)
            || values.Any(item => item.Value is null || item.GetChildren().Any()))
        { throw new InvalidOperationException(MembershipAuthoritySettingsProtocol.Invalid); }
    }
}
