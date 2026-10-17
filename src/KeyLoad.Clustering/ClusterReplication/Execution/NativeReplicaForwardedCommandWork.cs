using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Owns the single native lease, result ledger and budget of an incoming authenticated replica forward.</summary>
internal sealed class NativeReplicaForwardedCommandWork : IReplicaForwardedCommandWork, IAsyncDisposable
{
    private readonly NativeRequestWorkLease lease;
    private readonly Lock gate = new();
    private CancellationTokenSource? cancellation;
    private ResourceLifetimeWorkResultRetention? retention;
    private ReadExecutionBudget? budget;
    private Task? cancel;
    private Task<Exception?>? completion;

    private NativeReplicaForwardedCommandWork(NativeRequestWorkOwner owner)
        => lease = owner.Acquire(Guid.NewGuid(), NativeRequestWorkKind.CommandCapability);

    public ReadExecutionBudget Budget => budget
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
    public IResourceLifetimeWorkBinding Binding => lease;
    public CancellationToken Cancellation => cancellation?.Token
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);

    internal static async Task<IReplicaForwardedCommandWork> CreateAsync(NativeRequestWorkOwner owner,
        DatabaseEngine database, TimeProvider clock, CancellationToken incoming)
    {
        database.RequireResourceLifetimeWorkFenceAttached();
        var work = new NativeReplicaForwardedCommandWork(owner);
        try
        {
            work.retention = new(database.OperationLimitsOptions);
            work.lease.AttachResultRetention(work.retention);
            work.cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                incoming, owner.ShutdownToken, work.lease.Cancellation);
            work.budget = new(database.OperationLimitsOptions, clock, work.Cancellation);
            work.budget.AttachResourceLifetimeBinding(work.lease);
            work.budget.Check();
            return work;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            NativeRequestWorkSettlement.Rethrow(await work.CompleteAsync(error).ConfigureAwait(false));
            throw;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            NativeRequestWorkSettlement.Rethrow(await work.CompleteAsync(error).ConfigureAwait(false));
            throw;
        }
    }

    public void AdmitPublication() => lease.AdmitPublication();

    public Task CancelAsync()
    {
        lock (gate)
        {
            cancel ??= cancellation?.CancelAsync() ?? Task.CompletedTask;
            return cancel;
        }
    }

    public Task<Exception?> CompleteAsync(Exception? original)
    {
        lock (gate)
        {
            completion ??= CompleteCoreAsync(original);
            return completion;
        }
    }

    public async ValueTask DisposeAsync()
        => NativeRequestWorkSettlement.Rethrow(await CompleteAsync(null).ConfigureAwait(false));

    private async Task<Exception?> CompleteCoreAsync(Exception? original)
    {
        Exception? cleanup = null;
        try
        {
            Task? cancellationTask;
            lock (gate)
            {
                cancellationTask = cancel;
            }
            if (cancellationTask is not null)
            {
                cleanup = await NativeRequestWorkSettlement.JoinAsync(cancellationTask).ConfigureAwait(false);
            }
            await lease.PrepareCompletionAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return NativeRequestWorkSettlement.Preserve(original,
                NativeRequestWorkSettlement.Preserve(cleanup, error));
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return NativeRequestWorkSettlement.Preserve(original,
                NativeRequestWorkSettlement.Preserve(cleanup, error));
        }
        cleanup = DisposeOwned(cleanup);
        try
        {
            lease.PrepareDisposal(original, cleanup);
            await lease.DisposeAsync().ConfigureAwait(false);
            var outcome = await lease.Settlement.ConfigureAwait(false);
            return NativeRequestWorkSettlement.Preserve(outcome.OriginalFailure, outcome.CleanupOrJoinFailure);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return NativeRequestWorkSettlement.Preserve(original,
                NativeRequestWorkSettlement.Preserve(cleanup, error));
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return NativeRequestWorkSettlement.Preserve(original,
                NativeRequestWorkSettlement.Preserve(cleanup, error));
        }
    }

    private Exception? DisposeOwned(Exception? cleanup)
    {
        try
        {
            cancellation?.Dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            cleanup = NativeRequestWorkSettlement.Preserve(cleanup, error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            cleanup = NativeRequestWorkSettlement.Preserve(cleanup, error);
        }
        try
        {
            retention?.Dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            cleanup = NativeRequestWorkSettlement.Preserve(cleanup, error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            cleanup = NativeRequestWorkSettlement.Preserve(cleanup, error);
        }
        return cleanup;
    }
}
