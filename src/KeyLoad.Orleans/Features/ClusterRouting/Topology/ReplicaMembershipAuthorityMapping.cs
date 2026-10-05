using System.Collections.Immutable;

namespace KeyLoad.Orleans;

internal static class ReplicaMembershipAuthorityMapping
{
    internal static ReplicaMembershipAuthorityEntryV1 ToWire(MembershipEntry entry, string etag)
    {
        var row = ReplicaMembershipRow.From(entry, ParseEtag(etag));
        return new(row.Address, row.Status, row.ProxyPort, row.Host, row.Name, row.Started, row.Alive,
            ImmutableArray.CreateRange(row.Suspects), etag);
    }

    internal static MembershipEntry ToNative(ReplicaMembershipAuthorityEntryV1 entry)
    {
        ReplicaMembershipAuthorityValidation.Entry(entry);
        return new MembershipEntry
        {
            SiloAddress = SiloAddress.FromParsableString(entry.Address),
            Status = entry.Status,
            ProxyPort = entry.ProxyPort,
            HostName = entry.Host,
            SiloName = entry.Name,
            StartTime = entry.Started,
            IAmAliveTime = entry.Alive,
            SuspectTimes = entry.Suspects.Select(suspect => Tuple.Create(
                SiloAddress.FromParsableString(suspect.Address), suspect.Time)).ToList()
        };
    }

    internal static ImmutableArray<ReplicaMembershipAuthorityEntryV1> ToWireRows(MembershipTableData data)
        => data.Members.Select(member => ToWire(member.Item1, member.Item2)).ToImmutableArray();

    internal static ReplicaMembershipAuthorityErrorDetailCode Detail(ErrorCode code) => code switch
    {
        ErrorCode.Validation => ReplicaMembershipAuthorityErrorDetailCode.MalformedRequest,
        ErrorCode.Unauthenticated or ErrorCode.PermissionDenied => ReplicaMembershipAuthorityErrorDetailCode.AuthenticationFailed,
        ErrorCode.UnsupportedCapability or ErrorCode.FormatUnsupported => ReplicaMembershipAuthorityErrorDetailCode.UnsupportedVersion,
        ErrorCode.ResourceExhausted or ErrorCode.BudgetExceeded => ReplicaMembershipAuthorityErrorDetailCode.MembershipCapacity,
        ErrorCode.OwnershipLost or ErrorCode.UnknownWriteOutcome => ReplicaMembershipAuthorityErrorDetailCode.AuthorityUnavailable,
        ErrorCode.Corruption => ReplicaMembershipAuthorityErrorDetailCode.PersistedTableCorrupt,
        _ => ReplicaMembershipAuthorityErrorDetailCode.AuthorityUnavailable
    };

    private static long ParseEtag(string etag)
        => long.TryParse(etag, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
            out var value) && value >= 0 ? value : throw Errors.Fail(ErrorCode.Corruption,
            "The persisted membership ETag is invalid.");
}
