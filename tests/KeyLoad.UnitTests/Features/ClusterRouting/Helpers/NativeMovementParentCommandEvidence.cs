using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeMovementParentCommandEvidence
{
    private readonly Lock gate = new();
    private Snapshot? first;
    private Snapshot? latest;

    internal void Observe(GrainRequestProbeIdentity identity, GrainRequestPhase phase, CancellationToken token)
    {
        if (identity.CommandKind != OperationKind.MovePartition
            || phase is not (GrainRequestPhase.RequestStarted or GrainRequestPhase.ParentStagePreflight
                or GrainRequestPhase.ParentTransferCloseFailed or GrainRequestPhase.ParentReceiverIssueAcknowledged
                or GrainRequestPhase.ParentReceiverIssueObserved or GrainRequestPhase.ParentFinalInstallPreflight))
        { return; }
        lock (gate)
        {
            var snapshot = new Snapshot(identity.RequestId, identity.CommandId, phase,
                token.IsCancellationRequested, false);
            first ??= snapshot;
            if (first.RequestId == identity.RequestId)
            { first = snapshot; }
            latest = snapshot;
        }
    }

    internal void ProducerDisposed(GrainRequestProbeIdentity identity)
    {
        if (identity.CommandKind != OperationKind.MovePartition)
        { return; }
        lock (gate)
        {
            if (first?.RequestId == identity.RequestId)
            { first = first with { Disposed = true }; }
            if (latest?.RequestId == identity.RequestId)
            { latest = latest with { Disposed = true }; }
        }
    }

    internal void Write(TextWriter output, int configuredOwnerIndex)
    {
        Snapshot? original;
        Snapshot? last;
        lock (gate)
        { original = first; last = latest; }
        output.WriteLine($"Native movement parent configuredObserverOwnerIndex={configuredOwnerIndex}; First={original}; Latest={last}");
    }

    private sealed record Snapshot(Guid RequestId, Guid CommandId, GrainRequestPhase Phase,
        bool CancellationObservedAtPhase, bool Disposed);
}
