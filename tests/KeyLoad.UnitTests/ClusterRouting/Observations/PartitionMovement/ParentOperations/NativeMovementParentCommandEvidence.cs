using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeMovementParentCommandEvidence
{
    private readonly Lock gate = new();
    private Snapshot? first;
    private Snapshot? latest;
    private Snapshot? firstChild;
    private Snapshot? latestChild;

    internal void Observe(GrainRequestProbeIdentity identity, GrainRequestPhase phase, CancellationToken token)
    {
        var child = identity.CommandKind == OperationKind.PartitionMovementPhase
            && phase is (GrainRequestPhase.RequestStarted or GrainRequestPhase.AuthorizationReload
                or GrainRequestPhase.BeforeSubmit or GrainRequestPhase.SubmitReturned
                or GrainRequestPhase.RetireOperationSealed);
        var parent = identity.CommandKind == OperationKind.MovePartition
            && phase is (GrainRequestPhase.RequestStarted or GrainRequestPhase.ParentStagePreflight
                or GrainRequestPhase.ParentTransferCloseFailed or GrainRequestPhase.ParentReceiverIssueAcknowledged
                or GrainRequestPhase.ParentReceiverIssueObserved or GrainRequestPhase.ParentFinalInstallPreflight);
        if (!child && !parent)
        {
            return;
        }
        lock (gate)
        {
            var snapshot = new Snapshot(identity.RequestId, identity.CommandId, phase,
                token.IsCancellationRequested, false);
            if (child)
            {
                firstChild ??= snapshot;
                if (firstChild.RequestId == identity.RequestId)
                {
                    firstChild = snapshot;
                }
                latestChild = snapshot;
                return;
            }
            first ??= snapshot;
            if (first.RequestId == identity.RequestId)
            {
                first = snapshot;
            }
            latest = snapshot;
        }
    }

    internal void ProducerDisposed(GrainRequestProbeIdentity identity)
    {
        if (identity.CommandKind is not (OperationKind.MovePartition or OperationKind.PartitionMovementPhase))
        {
            return;
        }
        lock (gate)
        {
            if (firstChild?.RequestId == identity.RequestId)
            {
                firstChild = firstChild with
                {
                    Disposed = true
                };
            }
            if (latestChild?.RequestId == identity.RequestId)
            {
                latestChild = latestChild with
                {
                    Disposed = true
                };
            }
            if (first?.RequestId == identity.RequestId)
            {
                first = first with
                {
                    Disposed = true
                };
            }
            if (latest?.RequestId == identity.RequestId)
            {
                latest = latest with
                {
                    Disposed = true
                };
            }
        }
    }

    internal void Write(TextWriter output, int configuredOwnerIndex)
    {
        Snapshot? original;
        Snapshot? last;
        Snapshot? originalChild;
        Snapshot? lastChild;
        lock (gate)
        {
            original = first;
            last = latest;
            originalChild = firstChild;
            lastChild = latestChild;
        }
        output.WriteLine($"Native movement parent configuredObserverOwnerIndex={configuredOwnerIndex}; First={original}; Latest={last}");
        output.WriteLine($"Native movement child configuredObserverOwnerIndex={configuredOwnerIndex}; First={originalChild}; Latest={lastChild}");
    }

    private sealed record Snapshot(Guid RequestId, Guid CommandId, GrainRequestPhase Phase,
        bool CancellationObservedAtPhase, bool Disposed);
}
