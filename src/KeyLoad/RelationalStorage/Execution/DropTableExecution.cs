using KeyLoad.Clustering;
using KeyLoad.Core;
using ManagedCode.Communication;

namespace KeyLoad.Server.ResourceExecution;

/// <summary>Submits the authorized table deletion through the execution database command owner.</summary>
internal sealed class DropTableExecution(PartitionHost partition, TimeProvider clock)
    : IDropTableExecution
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
        var work = new ReadExecutionBudget(clock, deadline.Token);
        work.ConstrainLifetime(originalExpiry);
        await partition.Coordinator.ReadBarrierAsync(deadline.Token);
        var principal = partition.Database.Store.Read(raw => partition.Database.Principal(
            work.CreateView(raw), principalId, clock.GetUtcNow()));
        GrainRequestAuthority.RequireAdministrator(principal);
        return await partition.Coordinator.RuntimeSubmitAsync(OperationKind.DropTable, commandId, principal.Id,
            request, work, deadline.Token);
    }
}
