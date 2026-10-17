using System.Collections.Immutable;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Retains exact original outcomes even when cancel, cleanup or join fails.</summary>
internal sealed class NativeResourceWorkClosure : IResourceLifetimeWorkSettlement
{
    private readonly NativeRequestWorkOwner owner;
    private readonly ImmutableArray<NativeResourceWorkClosingLease> leases;
    private readonly ResourceRetirementOwnerFenceV1 fence;
    private readonly PhysicalShardRecord actualOwner;
    private readonly ImmutableArray<NativeResourceWorkSnapshot> snapshots;
    private readonly Lazy<Task<NativeResourceWorkClosureReceipt>> joining;
    private NativeResourceWorkClosureReport? terminalReport;

    internal NativeResourceWorkClosure(NativeRequestWorkOwner owner, ResourceRetirementOwnerFenceV1 fence, ImmutableArray<byte> fingerprint,
        PhysicalShardRecord actualOwner, ImmutableArray<NativeResourceWorkClosingLease> leases)
    {
        this.owner = owner;
        this.leases = leases;
        this.fence = fence;
        this.actualOwner = actualOwner;
        snapshots = leases.Select(item => item.Lease.RetirementSnapshot(fence, fingerprint)).ToImmutableArray();
        joining = new(JoinOriginalsAsync);
    }

    Task IResourceLifetimeWorkSettlement.CancelAndJoinAsync() => joining.Value;
    internal Task<NativeResourceWorkClosureReceipt> CancelAndJoinAsync() => joining.Value;
    internal NativeResourceWorkClosureReport? TerminalReport => Volatile.Read(ref terminalReport);
    internal ResourceRetirementOwnerFenceV1 Fence => fence;
    internal ImmutableArray<NativeResourceWorkClosingLease> OriginalLeases => leases;
    internal bool HasCompletedOriginalJoin => joining.IsValueCreated && joining.Value.IsCompleted;

    private async Task<NativeResourceWorkClosureReceipt> JoinOriginalsAsync()
    {
        var cancellations = leases.Where(item => item.Cancel).Select(item => item.Lease.CancelAsync()).ToArray();
        var cancelFailures = ImmutableArray.CreateBuilder<Exception>();
        var releaseFailures = ImmutableArray.CreateBuilder<Exception>();
        var joinFailures = ImmutableArray.CreateBuilder<Exception>();
        foreach (var cancellation in cancellations)
        {
            Add(await ObserveCancellationAsync(cancellation).ConfigureAwait(false), cancelFailures);
        }
        foreach (var item in leases.Where(item => item.Cancel))
        {
            Add(ReleaseObserver(item.Lease), releaseFailures);
        }
        var outcomes = ImmutableArray.CreateBuilder<NativeRequestWorkOriginalSettlement>();
        foreach (var item in leases)
        {
            await ObserveSettlementAsync(item.Lease, outcomes, joinFailures).ConfigureAwait(false);
        }
        var receipt = new NativeResourceWorkClosureReceipt(fence, actualOwner, snapshots, outcomes.ToImmutable());
        var report = new NativeResourceWorkClosureReport(receipt, cancelFailures.ToImmutable(),
            releaseFailures.ToImmutable(), joinFailures.ToImmutable());
        Volatile.Write(ref terminalReport, report);
        NativeRequestWorkSettlement.Rethrow(report.Failure);
        return receipt;
    }

    private static async Task<Exception?> ObserveCancellationAsync(Task<Exception?> original)
    {
        try
        {
            return await original.ConfigureAwait(false);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
    }

    private Exception? ReleaseObserver(NativeRequestWorkLease lease)
    {
        try
        {
            owner.ReleaseCloseObserver(lease);
            return null;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
    }

    private static async Task ObserveSettlementAsync(NativeRequestWorkLease lease,
        ImmutableArray<NativeRequestWorkOriginalSettlement>.Builder outcomes, ImmutableArray<Exception>.Builder failures)
    {
        try
        {
            outcomes.Add(await lease.Settlement.ConfigureAwait(false));
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
    }

    private static void Add(Exception? failure, ImmutableArray<Exception>.Builder failures)
    {
        if (failure is not null)
        {
            failures.Add(failure);
        }
    }
}
