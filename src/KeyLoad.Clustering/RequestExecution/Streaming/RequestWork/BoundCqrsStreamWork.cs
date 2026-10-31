using KeyLoad.Core.ResourceExecution;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>Owns the original call's linked cancellation and result ledger until its enumerator settles.</summary>
internal sealed class BoundCqrsStreamWork : IAsyncDisposable
{
    private CancellationTokenSource? request;
    private CancellationTokenSource? execution;
    private CancellationTokenSource? selected;
    private RequestWorkLease? lease;
    private ResourceLifetimeWorkResultRetention? ownedRetention;
    private Exception? admissionFailure;
    private CqrsStreamPurpose? originalPurpose;
    private CancellationToken originalToken;
    private IAsyncEnumerator<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>? originalEnumerator;
    private Func<ValueTask>? originalSettled;
    private Exception? originalPrimary;
    internal CancellationToken Token => selected?.Token ?? execution?.Token ?? request?.Token ?? originalToken;

    internal void Start(RequestWorkOwner owner, CqrsStreamPurpose purpose,
        Func<RequestWorkLease> acquireVerified, CancellationToken cancellationToken, CancellationToken enumerationToken)
    {
        originalPurpose = purpose;
        originalToken = cancellationToken;
        try
        {
            request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, enumerationToken);
            execution = CancellationTokenSource.CreateLinkedTokenSource(request.Token, owner.ShutdownToken);
            lease = acquireVerified();
            ownedRetention = new();
            lease.AttachResultRetention(ownedRetention);
            _ = lease.ResultRetention;
            selected = CancellationTokenSource.CreateLinkedTokenSource(execution.Token, lease.Cancellation);
            purpose.SetWork(lease);
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            admissionFailure = error;
            purpose.AdmissionFailed(error);
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
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
                RequestWorkSettlement.Rethrow(admissionFailure);
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
        var cleanup = await CqrsStreamSettlement.SettleAwaitedAsync(originalEnumerator,
            settled, null).ConfigureAwait(true);
        cleanup = DisposeCancellation(cleanup);
        var original = originalPrimary ?? originalPurpose?.OriginalFailure ?? admissionFailure;
        var preparation = (await PrepareAsync().ConfigureAwait(true)).Value;
        cleanup = RequestWorkSettlement.Preserve(cleanup, preparation);
        if (preparation is not null)
        {
            RequestWorkSettlement.Rethrow(RequestWorkSettlement.Preserve(original, cleanup));
            return;
        }
        cleanup = DisposeOwnedRetention(cleanup);
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
            catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
            {
                cleanup = RequestWorkSettlement.Preserve(previous, error);
            }
            catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
            {
                cleanup = RequestWorkSettlement.Preserve(previous, error);
            }
        }
        RequestWorkSettlement.Rethrow(cleanup is null ? null : RequestWorkSettlement.Preserve(original, cleanup));
    }

    private Exception? DisposeCancellation(Exception? cleanup)
    {
        cleanup = Dispose(selected, cleanup);
        cleanup = Dispose(execution, cleanup);
        return Dispose(request, cleanup);
    }

    private Exception? DisposeOwnedRetention(Exception? previous)
    {
        try
        {
            ownedRetention?.Dispose();
            return previous;
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return RequestWorkSettlement.Preserve(previous, error);
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            return RequestWorkSettlement.Preserve(previous, error);
        }
    }

    private async Task<(bool HasValue, Exception? Value)> PrepareAsync()
    {
        try
        {
            if (lease is not null)
            {
                await lease.PrepareCompletionAsync().ConfigureAwait(true);
            }
            return (false, default(Exception?));
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return (true, ((Exception?)(error)));
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            return (true, ((Exception?)(error)));
        }
    }

    private static Exception? Dispose(CancellationTokenSource? value, Exception? previous)
    {
        try
        {
            value?.Dispose();
            return previous;
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return RequestWorkSettlement.Preserve(previous, error);
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            return RequestWorkSettlement.Preserve(previous, error);
        }
    }
}
