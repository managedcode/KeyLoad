using KeyLoad.Clustering;
using KeyLoad.Core;
using ManagedCode.Communication;

namespace KeyLoad.Server.ResourceExecution;

/// <summary>Submits the authorized table deletion through the execution database command owner.</summary>
internal sealed class DropTableExecution(PartitionHost partition, TimeProvider clock)
    : IDropTableExecution
{
    private const string InvalidRequest = "The table deletion request identity is invalid.";

    public async Task<Result<object>> ExecuteAsync(Guid requestId, Guid commandId, string principalId,
        DropTableRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (requestId == Guid.Empty || commandId == Guid.Empty || request is null)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
        var work = new ReadExecutionBudget(clock, cancellationToken);
        await partition.Coordinator.ReadBarrierAsync(cancellationToken);
        var principal = partition.Database.Store.Read(raw => partition.Database.Principal(
            work.CreateView(raw), principalId, clock.GetUtcNow()));
        GrainRequestAuthority.RequireAdministrator(principal);
        return await partition.Coordinator.RuntimeSubmitAsync(OperationKind.DropTable, commandId, principal.Id,
            request, work, cancellationToken);
    }
}
