using ManagedCode.Communication;
namespace KeyLoad.Clustering;

/// <summary>Executes the execution retirement protocol for an already authenticated connection-owned DROP request.</summary>
public interface IDropTableExecution
{
    /// <summary>Returns the original final result only after the actual all-owner execution prerequisites complete.</summary>
    /// <param name="requestId">The separate Orleans request identity.</param>
    /// <param name="commandId">The stable original command identity from the existing command envelope.</param>
    /// <param name="principalId">The verified subject; persisted authority is checked freshly at each execution step.</param>
    /// <param name="request">The original bounded public request.</param>
    /// <param name="cancellationToken">The original request cancellation.</param>
    /// <returns>The original execution operation result; successful JSON contains DropTableResult.</returns>
    Task<Result<object>> ExecuteAsync(Guid requestId, Guid commandId, string principalId,
        DropTableRequest request, CancellationToken cancellationToken);
}
