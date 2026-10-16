using System.Collections.Immutable;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Holds the lease's retirement and publication-observer state under its original owner gate.</summary>
internal struct NativeRequestWorkLeasePublication(Dictionary<ResourceLifetimeV1, NativeResourceWorkFenceAdmission> retirements)
{
    private const int NoRetirements = 0;
    private const int NoObservers = 0;
    private const int SingleObserver = 1;
    private TaskCompletionSource? cancelObservers;
    private int observerCount;
    private bool publicationClosed;

    internal bool HasRetirementHolds => retirements.Count != NoRetirements;
    internal bool Completing
    {
        get; private set;
    }

    internal void MarkRetirement(ResourceRetirementOwnerFenceV1 fence, ImmutableArray<byte> fingerprint, NativeRequestWorkLease lease)
    {
        if (retirements.TryGetValue(fence.Control.Lifetime, out var original))
        {
            NativeResourceWorkRetentions.RequireFingerprint(original.Fingerprint, fingerprint);
            return;
        }
        publicationClosed = true;
        retirements.Add(fence.Control.Lifetime, new(fingerprint, lease.Snapshot()));
    }

    internal NativeResourceWorkSnapshot RetirementSnapshot(ResourceRetirementOwnerFenceV1 fence, ImmutableArray<byte> fingerprint)
    {
        var original = retirements[fence.Control.Lifetime];
        NativeResourceWorkRetentions.RequireFingerprint(original.Fingerprint, fingerprint);
        return original.Snapshot;
    }

    internal void ConsumeRetirement(ResourceRetirementOwnerFenceV1 fence, ImmutableArray<byte> fingerprint)
    {
        _ = RetirementSnapshot(fence, fingerprint);
        retirements.Remove(fence.Control.Lifetime);
    }

    internal bool ClosePublication(bool retainCancellation)
    {
        publicationClosed = true;
        if (Completing || !retainCancellation)
        {
            return false;
        }
        if (observerCount == NoObservers)
        {
            cancelObservers = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        observerCount = checked(observerCount + SingleObserver);
        return true;
    }

    internal void RetainCompletionObserver()
    {
        RequirePublicationOpen();
        if (observerCount == NoObservers)
        {
            cancelObservers = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        observerCount = checked(observerCount + SingleObserver);
    }

    internal Task BeginCompletion()
    {
        Completing = true;
        publicationClosed = true;
        return cancelObservers?.Task ?? Task.CompletedTask;
    }

    internal TaskCompletionSource? ReleaseCloseObserver()
        => --observerCount == NoObservers ? cancelObservers : null;

    internal void RequirePublicationOpen()
    {
        if (publicationClosed || Completing)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.AdmissionClosedMessage);
        }
    }
}
