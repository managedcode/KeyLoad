using KeyLoad.Clustering;
using KeyLoad.Core;
using ManagedCode.Communication;

namespace KeyLoad.Server.ResourceExecution;

/// <summary>Submits the authorized table deletion through the execution database command owner.</summary>
internal sealed class DropTableExecution(PartitionHost partition, TimeProvider clock)
    : IDropTableExecution
{
    private const string InvalidRequest = "The table deletion request identity is invalid.";

    public async Task<Result<DropTableResult>> ExecuteAsync(Guid requestId, Guid commandId, string principalId,
        DropTableRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (requestId == Guid.Empty || commandId == Guid.Empty || request is null)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
        var work = new ReadExecutionBudget(cancellationToken);
        await partition.Coordinator.ReadBarrierAsync(cancellationToken);
        var principal = partition.Database.Store.Read(raw => partition.Database.Principal(
            work.CreateView(raw), principalId, clock.GetUtcNow()));
        GrainRequestAuthority.RequireAdministrator(principal);
        return await (partition.Coordinator.RuntimeSubmitAsync<DropTableResult>(OperationKind.DropTable, commandId, principal.Id,
            request, work, cancellationToken) /* KLD0041: The existing coordinator creates the current DatabaseCommand.Input wire slot from this concrete DropTableRequest; the asynchronous result remains Result<DropTableResult> throughout. */);
    }
}
