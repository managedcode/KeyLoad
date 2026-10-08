using System.Security.Cryptography;

namespace KeyLoad.Server.Features.ClusterRouting;

[ConfigurationBinding]
internal static class MembershipAuthoritySettingsValidator
{
    private const int ValidOriginValueEmptyCount = 0;
    private const char SiloHostColonCharacter = ':';
    private const int HasAuthorityFieldsEmptyAuthorityEndpointsLength = 0;
    private const int HasTrustedFieldsEmptyTrustedGroupVoterIdsLength = 0;
    private const int HasTrustedFieldsEmptyTrustedGroupSiloEndpointsLength = 0;

    private const string RootPath = "/";

    internal static void Validate(MembershipAuthoritySettings settings, NodeOptions node)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(node);
        if (settings.Mode == MembershipAuthoritySettingsProtocol.Local)
        {
            if (settings.RemotePartitionQueries || settings.RemoteDocumentReads || settings.RegisterPhysicalOwners || HasAuthorityFields(settings) || HasTrustedFields(settings))
            { throw new InvalidOperationException(MembershipAuthoritySettingsProtocol.Invalid); }
            return;
        }
        if (settings.RemotePartitionQueries && !settings.RemoteDocumentReads)
        { throw new InvalidOperationException(MembershipAuthoritySettingsProtocol.Invalid); }
        if (settings.RemoteDocumentReads && !settings.RegisterPhysicalOwners)
        { throw new InvalidOperationException(MembershipAuthoritySettingsProtocol.Invalid); }
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
            MembershipAuthoritySettingsProtocol.Local => new[] { nameof(MembershipAuthoritySettings.Mode) },
            MembershipAuthoritySettingsProtocol.Authority => new[]
            { nameof(MembershipAuthoritySettings.Mode), nameof(MembershipAuthoritySettings.RegisterPhysicalOwners), nameof(MembershipAuthoritySettings.RemoteDocumentReads), nameof(MembershipAuthoritySettings.RemotePartitionQueries), nameof(MembershipAuthoritySettings.TrustedGroupPhysicalShardId), nameof(MembershipAuthoritySettings.TrustedGroupIncarnation), nameof(MembershipAuthoritySettings.TrustedGroupVoterIds), nameof(MembershipAuthoritySettings.TrustedGroupSiloEndpoints), nameof(MembershipAuthoritySettings.TrustedGroupPeerSecret) },
            MembershipAuthoritySettingsProtocol.Proxy => new[]
            { nameof(MembershipAuthoritySettings.Mode), nameof(MembershipAuthoritySettings.RegisterPhysicalOwners), nameof(MembershipAuthoritySettings.RemoteDocumentReads), nameof(MembershipAuthoritySettings.RemotePartitionQueries), nameof(MembershipAuthoritySettings.AuthorityPhysicalShardId), nameof(MembershipAuthoritySettings.AuthorityIncarnation), nameof(MembershipAuthoritySettings.AuthorityEndpoints), nameof(MembershipAuthoritySettings.AuthorityPeerSecret) },
            _ => []
        };
        if (children.Any(child => !allowed.Contains(child.Key, StringComparer.Ordinal))
            || children.Any(child => child.Value is not null && child.GetChildren().Any()))
        { throw new InvalidOperationException(MembershipAuthoritySettingsProtocol.Invalid); }
        ValidateArray(section, nameof(MembershipAuthoritySettings.AuthorityEndpoints), settings.Mode == MembershipAuthoritySettingsProtocol.Proxy);
        ValidateArray(section, nameof(MembershipAuthoritySettings.TrustedGroupVoterIds), settings.Mode == MembershipAuthoritySettingsProtocol.Authority);
        ValidateArray(section, nameof(MembershipAuthoritySettings.TrustedGroupSiloEndpoints), settings.Mode == MembershipAuthoritySettingsProtocol.Authority);
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
        const int IndexInitialValue = 0;

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
        for (var index = IndexInitialValue; index < MembershipAuthoritySettingsProtocol.RequiredMembers; index++)
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
        => value is { Length: > ValidOriginValueEmptyCount and <= MembershipAuthoritySettingsProtocol.MaximumIdentityBytes }
            && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment) && uri.AbsolutePath == RootPath;

    private static bool ValidSiloEndpoint(string value)
    {
        const int ValueEmptyCount = 0;
        const char ColonCharacter = ':';
        const int SeparatorValidationBoundary = 0;
        const int ValueSecondIndex = 1;
        const char PredicateCharacter = '.';
        const char HostLabelHyphen = '-';

        if (value is not { Length: > ValueEmptyCount and <= MembershipAuthoritySettingsProtocol.MaximumIdentityBytes })
        { return false; }
        var separator = value.LastIndexOf(ColonCharacter);
        return separator > SeparatorValidationBoundary && int.TryParse(value[(separator + ValueSecondIndex)..], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var port)
            && port == MembershipAuthoritySettingsProtocol.NativeSiloPort
            && string.Equals(value, value.Trim(), StringComparison.Ordinal)
            && value[..separator].All(character => char.IsAsciiLetterOrDigit(character) || character is PredicateCharacter or HostLabelHyphen);
    }

    private static string SiloHost(string value) => value[..value.LastIndexOf(SiloHostColonCharacter)];

    private static bool ValidSecret(string? value)
    {
        const int ValueValidationBound = 44;

        Span<byte> bytes = stackalloc byte[MembershipAuthoritySettingsProtocol.SecretBytes];
        try
        {
            return value is { Length: ValueValidationBound } && Convert.TryFromBase64String(value, bytes, out var count)
                && count == bytes.Length && string.Equals(Convert.ToBase64String(bytes), value, StringComparison.Ordinal);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static bool HasAuthorityFields(MembershipAuthoritySettings settings)
        => settings.AuthorityPhysicalShardId != Guid.Empty || settings.AuthorityIncarnation != Guid.Empty
            || settings.AuthorityEndpoints.Length != HasAuthorityFieldsEmptyAuthorityEndpointsLength || settings.AuthorityPeerSecret is not null;

    private static bool HasTrustedFields(MembershipAuthoritySettings settings)
        => settings.TrustedGroupPhysicalShardId != Guid.Empty || settings.TrustedGroupIncarnation != Guid.Empty
            || settings.TrustedGroupVoterIds.Length != HasTrustedFieldsEmptyTrustedGroupVoterIdsLength || settings.TrustedGroupSiloEndpoints.Length != HasTrustedFieldsEmptyTrustedGroupSiloEndpointsLength
            || settings.TrustedGroupPeerSecret is not null;

    private static void ValidateArray(IConfigurationSection section, string name, bool required)
    {
        const int RequiredMembersStep = 1;
        const int EmptyValuesLength = 0;
        const string ValidateArraySecondText = "0";
        const string ValidateArrayValidateArraySecondText = "1";
        const string ThirdVoterIndex = "2";

        var child = section.GetSection(name);
        var values = child.GetChildren().Take(MembershipAuthoritySettingsProtocol.RequiredMembers + RequiredMembersStep).ToArray();
        if (child.Value is not null || required && values.Length != MembershipAuthoritySettingsProtocol.RequiredMembers
            || !required && values.Length != EmptyValuesLength
            || required && !values.Select(item => item.Key).SequenceEqual([ValidateArraySecondText, ValidateArrayValidateArraySecondText, ThirdVoterIndex], StringComparer.Ordinal)
            || values.Any(item => item.Value is null || item.GetChildren().Any()))
        { throw new InvalidOperationException(MembershipAuthoritySettingsProtocol.Invalid); }
    }
}
