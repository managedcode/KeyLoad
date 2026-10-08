using System.Text;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PhysicalOwnerEntryValidation
{
    private const string HttpScheme = "http";
    private const string HttpsScheme = "https";
    private const string RootPath = "/";
    private const int EmptyLength = 0;

    internal static bool Valid(RegisteredPhysicalOwnerV1? entry)
    {
        if (entry is null)
        { return false; }
        var owner = entry.Owner;
        if (owner is null || owner.PhysicalShardId == Guid.Empty || owner.Incarnation == Guid.Empty
            || owner.PlacementEpoch != PhysicalOwnerDirectoryProtocol.InitialEpoch
            || owner.VoterIds.IsDefault || owner.VoterIds.Length != PhysicalOwnerDirectoryProtocol.VoterCount
            || entry.Endpoints.IsDefault || entry.Endpoints.Length != PhysicalOwnerDirectoryProtocol.VoterCount)
        { return false; }
        return owner.VoterIds.All(ValidVoter)
            && owner.VoterIds.Distinct(StringComparer.Ordinal).Count() == owner.VoterIds.Length
            && entry.Endpoints.All(ValidEndpoint)
            && entry.Endpoints.Distinct(StringComparer.Ordinal).Count() == entry.Endpoints.Length;
    }

    private static bool ValidVoter(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && Encoding.UTF8.GetByteCount(value) <= PhysicalShardCatalogProtocol.MaximumVoterIdBytes;

    private static bool ValidEndpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || Encoding.UTF8.GetByteCount(value) > PhysicalOwnerDirectoryProtocol.MaximumEndpointBytes
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        { return false; }
        return uri.Scheme is HttpScheme or HttpsScheme && uri.UserInfo.Length == EmptyLength
            && uri.Query.Length == EmptyLength && uri.Fragment.Length == EmptyLength
            && uri.AbsolutePath == RootPath && uri.AbsoluteUri == value;
    }

    internal static bool Same(RegisteredPhysicalOwnerV1 left, RegisteredPhysicalOwnerV1 right)
        => SameOwner(left.Owner, right.Owner) && left.Endpoints.SequenceEqual(right.Endpoints, StringComparer.Ordinal);

    internal static bool SameOwner(PhysicalShardRecord left, PhysicalShardRecord right)
        => left.PhysicalShardId == right.PhysicalShardId && left.Incarnation == right.Incarnation
            && left.PlacementEpoch == right.PlacementEpoch
            && left.VoterIds.SequenceEqual(right.VoterIds, StringComparer.Ordinal);
}
