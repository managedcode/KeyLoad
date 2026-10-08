using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>Exact retained scope and immutable page owner for one admitted source capability.</summary>
internal sealed class PartitionMovementSourceEntry(PartitionMovePeerEnvelope verified,
    PartitionMovementCaptureHandle handle, PartitionMovementImageSession session, NativeRequestWorkLease work)
{
    private int workReleased;
    internal PartitionRef Partition => verified.Partition;
    internal Guid MoveId => verified.MoveId;
    internal Guid CommandId => verified.Grant!.PhaseCommandId;
    internal PartitionMovementCaptureHandle Handle { get; } = handle;
    internal PartitionMovementImageSession Session { get; } = session;
    internal void ReleaseWork()
    {
        if (Interlocked.Exchange(ref workReleased, SingleRelease) == NotReleased)
        { work.Dispose(); }
    }
    private const int SingleRelease = 1;
    private const int NotReleased = 0;
    private const string Unavailable = "The partition movement source capability is unavailable.";
    private readonly string scope = JsonData.Fingerprint(verified.Grant!);

    internal void Require(PartitionMovePeerEnvelope actual, DateTimeOffset now)
    {
        if (actual.Grant is null || actual.Stage != PartitionMovePeerStage.Capture
            || actual.ExpiresAt != verified.ExpiresAt || actual.ExpiresAt <= now
            || actual.MoveId != verified.MoveId || actual.Partition != verified.Partition
            || JsonData.Fingerprint(actual.ControlOwner) != JsonData.Fingerprint(verified.ControlOwner)
            || JsonData.Fingerprint(actual.SourcePlacement) != JsonData.Fingerprint(verified.SourcePlacement)
            || JsonData.Fingerprint(actual.DestinationOwner) != JsonData.Fingerprint(verified.DestinationOwner)
            || actual.ControlIntentDigest != verified.ControlIntentDigest
            || JsonData.Fingerprint(actual.Grant) != scope
            || !actual.Body.Span.SequenceEqual(verified.Body.Span))
        { throw Errors.Fail(ErrorCode.OwnershipLost, Unavailable); }
    }
}
