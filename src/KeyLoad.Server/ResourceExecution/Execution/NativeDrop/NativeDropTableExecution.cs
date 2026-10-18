using KeyLoad.Clustering;
using KeyLoad.Core;
using ManagedCode.Communication;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Submits the authorized table deletion through the native database command owner.</summary>
internal sealed class NativeDropTableExecution(PartitionHostApplicationOwner owner, TimeProvider clock)
    : INativeDropTableExecution
{
    private const string InvalidRequest = "The table deletion request identity or deadline is invalid.";

    public async Task<Result<object>> ExecuteAsync(Guid requestId, Guid commandId, string principalId,
        DropTableRequest request, DateTimeOffset originalExpiry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (requestId == Guid.Empty || commandId == Guid.Empty || request is null)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
        var remaining = originalExpiry - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            throw Errors.Fail(ErrorCode.CursorExpired, InvalidRequest);
        }
        using var timeout = new CancellationTokenSource(remaining, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var partition = owner.Borrow();
        var work = new ReadExecutionBudget(partition.Database.OperationLimitsOptions, clock, deadline.Token);
        work.ConstrainLifetime(originalExpiry);
        await partition.Coordinator.ReadBarrierAsync(deadline.Token).ConfigureAwait(false);
        var principal = partition.Database.Store.Read(raw => partition.Database.Principal(
            work.CreateView(raw), principalId, clock.GetUtcNow()));
        GrainRequestAuthority.RequireAdministrator(principal);
        return await partition.Coordinator.SubmitNativeAsync(OperationKind.DropTable, commandId, principal.Id,
            NativeSerialization.Serialize(request), work, deadline.Token).ConfigureAwait(false);
    }
}
