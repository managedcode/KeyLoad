using System.Collections.Immutable;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>Closed native heartbeat shape; it cannot replace a complete membership row.</summary>
internal static class ReplicaMembershipAuthorityHeartbeat
{
    private const SiloStatus EmptyHeartbeatStatus = (SiloStatus)0;
    private const int EmptyHeartbeatProxyPort = 0;
    private const int EmptyHeartbeatMetadataLength = 0;
    private const string EmptyHeartbeatMetadata = "";
    private const string InvalidHeartbeat = "The membership authority request is invalid.";

    internal static ReplicaMembershipAuthorityEntryV1 ToWire(MembershipEntry entry)
        => new(entry.SiloAddress.ToParsableString(), EmptyHeartbeatStatus, EmptyHeartbeatProxyPort,
            EmptyHeartbeatMetadata, EmptyHeartbeatMetadata, default, entry.IAmAliveTime,
            ImmutableArray<ReplicaMembershipSuspect>.Empty, ReplicaMembershipAuthorityProtocol.InitialRowETag);

    internal static void Validate(ReplicaMembershipAuthorityEntryV1 entry,
        IOptions<OrleansMembershipOptions> membershipOptions)
    {
        if (entry is null || !ReplicaMembershipAuthorityValidation.CanonicalAddress(entry.Address, membershipOptions)
            || entry.Status != EmptyHeartbeatStatus || entry.ProxyPort != EmptyHeartbeatProxyPort
            || entry.Host is not { Length: EmptyHeartbeatMetadataLength }
            || entry.Name is not { Length: EmptyHeartbeatMetadataLength }
            || entry.Started != default || entry.Alive.Kind != DateTimeKind.Utc
            || entry.Suspects.IsDefault || !entry.Suspects.IsEmpty
            || entry.RowETag != ReplicaMembershipAuthorityProtocol.InitialRowETag)
        { throw Errors.Fail(ErrorCode.Validation, InvalidHeartbeat); }
        if (NativeSerialization.Measure(entry) > membershipOptions.Value.MaximumRowBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidHeartbeat); }
    }

    internal static MembershipEntry ToNative(ReplicaMembershipAuthorityEntryV1 entry,
        IOptions<OrleansMembershipOptions> membershipOptions)
    {
        Validate(entry, membershipOptions);
        return new() { SiloAddress = SiloAddress.FromParsableString(entry.Address), IAmAliveTime = entry.Alive };
    }
}
