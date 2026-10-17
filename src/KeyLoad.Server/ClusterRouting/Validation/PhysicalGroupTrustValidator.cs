using System.Globalization;
using System.Security.Cryptography;

namespace KeyLoad.Server.Features.ClusterRouting;

[ConfigurationBinding]
internal static class PhysicalGroupTrustValidator
{
    private const int EmptyCount = 0;
    private const int LookaheadCount = 1;
    private const string RootPath = "/";

    internal static void Validate(PhysicalGroupTrustOptions trust, NodeOptions node)
    {
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(node);
        if (trust.ForeignVoterIds is null)
        {
            throw new InvalidOperationException(PhysicalGroupTrustProtocol.Invalid);
        }
        if (trust.Role == PhysicalGroupTrustRole.Disabled)
        {
            RequireDisabled(trust);
            return;
        }
        if (trust.Role is not (PhysicalGroupTrustRole.Control or PhysicalGroupTrustRole.Destination)
            || trust.RemotePartitionQueries && !trust.RemoteDocumentReads
            || trust.RemoteDocumentReads && !trust.RegisterPhysicalOwners
            || node.BenchmarkTopology || !node.AllowPrivateNetworkHttp
            || !node.Peers.Any()
            || string.IsNullOrWhiteSpace(node.ClusterId) || node.PhysicalShardId == Guid.Empty || node.Incarnation == Guid.Empty
            || trust.ForeignPhysicalShardId == Guid.Empty || trust.ForeignIncarnation == Guid.Empty
            || trust.ForeignPhysicalShardId == node.PhysicalShardId || trust.ForeignIncarnation == node.Incarnation
            || trust.ForeignVoterIds.Length == EmptyCount
            || trust.ForeignVoterIds.Distinct(StringComparer.Ordinal).Count() != trust.ForeignVoterIds.Length
            || trust.ForeignVoterIds.Any(value => !ValidOrigin(value))
            || trust.ForeignVoterIds.Intersect(node.Peers, StringComparer.Ordinal).Any()
            || !ValidSecret(trust.ForeignPeerSecret)
            || string.Equals(trust.ForeignPeerSecret, node.PeerSecret, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(PhysicalGroupTrustProtocol.Invalid);
        }
    }

    internal static void ValidateConfiguration(IConfigurationSection nodeSection, PhysicalGroupTrustOptions trust)
    {
        ArgumentNullException.ThrowIfNull(nodeSection);
        const string RetiredSection = "MembershipAuthority";
        var retired = nodeSection.GetSection(RetiredSection);
        if (retired.Value is not null || retired.GetChildren().Any())
        {
            throw new InvalidOperationException(PhysicalGroupTrustProtocol.Invalid);
        }
        ValidateSection(nodeSection.GetSection(PhysicalGroupTrustProtocol.Section), trust);
    }

    internal static void ValidateSection(IConfigurationSection section, PhysicalGroupTrustOptions trust)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(trust);
        var allowed = trust.Role == PhysicalGroupTrustRole.Disabled
            ? new[] { nameof(PhysicalGroupTrustOptions.Role) }
            : new[] { nameof(PhysicalGroupTrustOptions.Role), nameof(PhysicalGroupTrustOptions.RegisterPhysicalOwners),
                nameof(PhysicalGroupTrustOptions.RemoteDocumentReads), nameof(PhysicalGroupTrustOptions.RemotePartitionQueries),
                nameof(PhysicalGroupTrustOptions.ForeignPhysicalShardId), nameof(PhysicalGroupTrustOptions.ForeignIncarnation),
                nameof(PhysicalGroupTrustOptions.ForeignPeerSecret), nameof(PhysicalGroupTrustOptions.ForeignVoterIds) };
        if (section.Value is not null || section.GetChildren().Any(child => !allowed.Contains(child.Key, StringComparer.Ordinal)
                || child.Key != nameof(PhysicalGroupTrustOptions.ForeignVoterIds) && child.GetChildren().Any()))
        {
            throw new InvalidOperationException(PhysicalGroupTrustProtocol.Invalid);
        }
        var array = section.GetSection(nameof(PhysicalGroupTrustOptions.ForeignVoterIds));
        var values = array.GetChildren().Take(checked(trust.ForeignVoterIds.Length + LookaheadCount)).ToArray();
        var expected = Enumerable.Range(EmptyCount, trust.ForeignVoterIds.Length)
            .Select(index => index.ToString(CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
        if (array.Value is not null
            || trust.Role == PhysicalGroupTrustRole.Disabled && values.Length != EmptyCount
            || trust.Role != PhysicalGroupTrustRole.Disabled && (values.Length != trust.ForeignVoterIds.Length || !expected.SetEquals(values.Select(value => value.Key)))
            || values.Any(value => value.Value is null || value.GetChildren().Any()))
        {
            throw new InvalidOperationException(PhysicalGroupTrustProtocol.Invalid);
        }
    }

    private static void RequireDisabled(PhysicalGroupTrustOptions trust)
    {
        if (trust.RemoteDocumentReads || trust.RemotePartitionQueries || trust.RegisterPhysicalOwners
            || trust.ForeignPhysicalShardId != Guid.Empty || trust.ForeignIncarnation != Guid.Empty
            || trust.ForeignVoterIds.Length != EmptyCount || trust.ForeignPeerSecret is not null)
        {
            throw new InvalidOperationException(PhysicalGroupTrustProtocol.Invalid);
        }
    }

    private static bool ValidOrigin(string value)
        => value is { Length: > EmptyCount and <= PhysicalGroupTrustProtocol.MaximumIdentityBytes }
            && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment) && uri.AbsolutePath == RootPath;

    private static bool ValidSecret(string? value)
    {
        Span<byte> bytes = stackalloc byte[PhysicalGroupTrustProtocol.SecretBytes];
        try
        {
            return value is { Length: PhysicalGroupTrustProtocol.EncodedSecretCharacters }
                && Convert.TryFromBase64String(value, bytes, out var count) && count == bytes.Length
                && string.Equals(Convert.ToBase64String(bytes), value, StringComparison.Ordinal);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
