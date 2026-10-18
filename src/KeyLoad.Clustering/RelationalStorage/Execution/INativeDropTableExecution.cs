using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
namespace KeyLoad.Clustering;

/// <summary>Executes the native retirement protocol for an already authenticated connection-owned DROP request.</summary>
public interface INativeDropTableExecution
{
    /// <summary>Returns the original final result only after the actual all-owner native prerequisites complete.</summary>
    /// <param name="requestId">The separate Orleans request identity.</param>
    /// <param name="commandId">The stable original command identity from the existing command envelope.</param>
    /// <param name="principalId">The verified subject; persisted authority is checked freshly at each native step.</param>
    /// <param name="request">The original bounded public request.</param>
    /// <param name="originalExpiry">The unchanged signed request expiry; native stages cannot renew it.</param>
    /// <param name="cancellationToken">The original request cancellation and deadline.</param>
    /// <returns>The original native operation result; successful JSON contains DropTableResult.</returns>
    Task<Result<object>> ExecuteAsync(Guid requestId, Guid commandId, string principalId,
        DropTableRequest request, DateTimeOffset originalExpiry, CancellationToken cancellationToken);
}
