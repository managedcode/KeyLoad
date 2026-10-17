using System.Runtime.ExceptionServices;
using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Settles rejected admission or an admitted native producer before releasing its frame.</summary>
internal static class NativeRequestWorkStreamSettlement
{
    internal static async ValueTask<NativeRequestWorkLease?> AcquireOrSettleAsync(
        NativeRequestWorkOwner? owner, Guid requestId, Action settled)
        => await AcquireOrSettleAwaitedAsync(owner, requestId, () => { settled(); return ValueTask.CompletedTask; }).ConfigureAwait(true);

    internal static async ValueTask<NativeRequestWorkLease?> AcquireOrSettleAwaitedAsync(
        NativeRequestWorkOwner? owner, Guid requestId, Func<ValueTask> settled)
    {
        var (lease, error) = TryAcquire(owner, requestId);
        if (error is null)
        {
            return lease;
        }

        var failure = await SettleAndReleaseAwaitedAsync<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>(
            null, settled, error, null).ConfigureAwait(true);
        ExceptionDispatchInfo.Capture(failure ?? error).Throw();
        return null;
    }

    private static (NativeRequestWorkLease? Lease, Exception? Error) TryAcquire(
        NativeRequestWorkOwner? owner, Guid requestId)
    {
        if (owner is null)
        {
            return (null, null);
        }

        try
        {
            return (owner.Acquire(requestId, NativeRequestWorkKind.RequestProducer), null);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return (null, error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return (null, error);
        }
    }

    internal static async ValueTask<Exception?> SettleAndReleaseAsync<T>(IAsyncEnumerator<T>? enumerator,
        Action settled, Exception? primary, NativeRequestWorkLease? lease)
        => await SettleAndReleaseAwaitedAsync(enumerator, () => { settled(); return ValueTask.CompletedTask; }, primary, lease).ConfigureAwait(true);

    internal static async ValueTask<Exception?> SettleAndReleaseAwaitedAsync<T>(IAsyncEnumerator<T>? enumerator,
        Func<ValueTask> settled, Exception? primary, NativeRequestWorkLease? lease)
    {
        Exception? failure = null;
        Exception? settlementError = null;
        try
        {
            failure = await NativeCqrsStreamSettlement.SettleAwaitedAsync(enumerator, settled, primary).ConfigureAwait(true);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            settlementError = error;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            settlementError = error;
        }

        var releaseFailure = Release(lease);
        if (settlementError is not null)
        {
            failure = NativeRequestWorkSettlement.Preserve(primary, settlementError);
        }

        return releaseFailure is null ? failure
            : NativeRequestWorkSettlement.Preserve(failure ?? primary, releaseFailure);
    }

    private static Exception? Release(NativeRequestWorkLease? lease)
    {
        if (lease is null)
        {
            return null;
        }

        try
        {
            lease.Dispose();
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
}

/// <summary>Owns only the original call's linked cancellation and, for a root producer, its one result ledger.</summary>
internal sealed class NativeBoundCqrsStreamWork : IAsyncDisposable
{
    private CancellationTokenSource? deadline;
    private CancellationTokenSource? request;
    private CancellationTokenSource? ownerRequest;
    private CancellationTokenSource? execution;
    private CancellationTokenSource? selected;
    private NativeRequestWorkLease? lease;
    private ResourceLifetimeWorkResultRetention? ownedRetention;
    private Exception? admissionFailure;
    private NativeCqrsStreamPurpose? originalPurpose;
    private CancellationToken originalToken;
    private IAsyncEnumerator<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>? originalEnumerator;
    private Func<ValueTask>? originalSettled;
    private Exception? originalPrimary;
    internal CancellationToken Token => selected?.Token ?? execution?.Token ?? request?.Token ?? originalToken;

    internal void Start(TimeProvider clock, IOptions<GrainRoutingOptions> options, NativeRequestWorkOwner owner,
        NativeCqrsStreamPurpose purpose, IOptions<DatabaseLimits> databaseLimits,
        Func<NativeRequestWorkLease> acquireVerified, CancellationToken cancellationToken, CancellationToken enumerationToken)
    {
        originalPurpose = purpose;
        originalToken = cancellationToken;
        try
        {
            deadline = new(options.Value.ExecutionLifetime, clock);
            request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, enumerationToken);
            ownerRequest = CancellationTokenSource.CreateLinkedTokenSource(request.Token, owner.ShutdownToken);
            execution = CancellationTokenSource.CreateLinkedTokenSource(ownerRequest.Token, deadline.Token);
            lease = acquireVerified();
            if (lease.OwnsResultRetention)
            {
                ownedRetention = new(databaseLimits);
                lease.AttachResultRetention(ownedRetention);
            }
            _ = lease.ResultRetention;
            selected = CancellationTokenSource.CreateLinkedTokenSource(execution.Token, lease.Cancellation);
            purpose.SetWork(lease);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            admissionFailure = error;
            purpose.AdmissionFailed(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            admissionFailure = error;
            purpose.AdmissionFailed(error);
        }
    }

    internal void AdmitPublication(CqrsStreamChunkKind kind)
    {
        if (admissionFailure is not null)
        {
            if (kind != CqrsStreamChunkKind.Failed)
            {
                NativeRequestWorkSettlement.Rethrow(admissionFailure);
            }
            return;
        }
        (lease ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest)).AdmitPublication();
    }

    // Set once by the original enumerator finally; DisposeAsync owns its complete settlement.
    internal void PrepareSettlement(IAsyncEnumerator<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>? enumerator,
        Func<ValueTask> settled, Exception? primary)
    {
        if (originalSettled is not null)
        {
            return;
        }
        originalEnumerator = enumerator;
        originalPrimary = primary;
        originalSettled = settled;
    }

    public async ValueTask DisposeAsync()
    {
        var settled = originalSettled ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        var cleanup = await NativeCqrsStreamSettlement.SettleAwaitedAsync(originalEnumerator,
            settled, null).ConfigureAwait(true);
        cleanup = DisposeCancellation(cleanup);
        var original = originalPrimary ?? originalPurpose?.OriginalFailure ?? admissionFailure;
        var preparation = await PrepareAsync().ConfigureAwait(true);
        cleanup = NativeRequestWorkSettlement.Preserve(cleanup, preparation);
        if (preparation is not null)
        {
            NativeRequestWorkSettlement.Rethrow(NativeRequestWorkSettlement.Preserve(original, cleanup));
            return;
        }
        cleanup = Dispose(ownedRetention, cleanup);
        if (lease is not null)
        {
            var previous = cleanup;
            try
            {
                lease.PrepareDisposal(original, cleanup);
                await lease.DisposeAsync().ConfigureAwait(true);
                var report = await lease.Settlement.ConfigureAwait(true);
                cleanup = report.CleanupOrJoinFailure;
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                cleanup = NativeRequestWorkSettlement.Preserve(previous, error);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                cleanup = NativeRequestWorkSettlement.Preserve(previous, error);
            }
        }
        NativeRequestWorkSettlement.Rethrow(cleanup is null ? null : NativeRequestWorkSettlement.Preserve(original, cleanup));
    }

    private Exception? DisposeCancellation(Exception? cleanup)
    {
        cleanup = Dispose(selected, cleanup);
        cleanup = Dispose(execution, cleanup);
        cleanup = Dispose(ownerRequest, cleanup);
        cleanup = Dispose(request, cleanup);
        return Dispose(deadline, cleanup);
    }

    private async Task<Exception?> PrepareAsync()
    {
        try
        {
            if (lease is not null)
            {
                await lease.PrepareCompletionAsync().ConfigureAwait(true);
            }
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

    private static Exception? Dispose(IDisposable? value, Exception? previous)
    {
        try
        {
            value?.Dispose();
            return previous;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return NativeRequestWorkSettlement.Preserve(previous, error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return NativeRequestWorkSettlement.Preserve(previous, error);
        }
    }
}
