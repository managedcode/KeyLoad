using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Retains bounded fixture-owned state for one immutable arm identity.</summary>
internal sealed class RequestCqrsProbeArmState(string principalId, Guid commandId,
    GrainReadKind? readKind, RequestCqrsProbePhase phase, RequestCqrsProbeAction action, byte[] bytes)
{
    internal byte[] Bytes { get; } = bytes;
    internal string PrincipalId { get; } = principalId;
    internal Guid CommandId { get; } = commandId;
    internal GrainReadKind? ReadKind { get; } = readKind;
    internal RequestCqrsProbePhase Phase { get; } = phase;
    internal RequestCqrsProbeAction Action { get; } = action;
    internal Guid ArmId { get; set; }
    internal Guid? RequestId { get; set; }
    internal bool ReleaseWritten { get; set; }
    internal bool Settled { get; set; }
    internal bool Retired { get; set; }
    internal bool ProducerDisposedSeen { get; set; }
    internal bool CanonicalOwnerDisposedSeen { get; set; }
    internal bool OwnerDisposedSeen => Phase == RequestCqrsProbePhase.CanonicalJournalFlushed
        ? CanonicalOwnerDisposedSeen : ProducerDisposedSeen;
    internal string? GateVoter { get; set; }
    internal RequestCqrsProbeMarkerRecord?[] MarkerRecords { get; } = new RequestCqrsProbeMarkerRecord?[
        RequestCqrsProbeFixtureProtocol.MaximumMarkerRecordsPerArm];
    internal int MarkerRecordCount { get; set; }
}
