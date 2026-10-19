using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Owns the original call's linked cancellation and result ledger until its enumerator settles.</summary>
internal sealed class BoundCqrsStreamWork : IAsyncDisposable
{
    private CancellationTokenSource? deadline;
    private CancellationTokenSource? request;
    private CancellationTokenSource? ownerRequest;
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

    internal void Start(TimeProvider clock, IOptions<GrainRoutingOptions> options, RequestWorkOwner owner,
        CqrsStreamPurpose purpose, IOptions<DatabaseLimits> databaseLimits,
        Func<RequestWorkLease> acquireVerified, CancellationToken cancellationToken, CancellationToken enumerationToken)
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
            ownedRetention = new(databaseLimits);
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
        var preparation = await PrepareAsync().ConfigureAwait(true);
        cleanup = RequestWorkSettlement.Preserve(cleanup, preparation);
        if (preparation is not null)
        {
            RequestWorkSettlement.Rethrow(RequestWorkSettlement.Preserve(original, cleanup));
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
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
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
