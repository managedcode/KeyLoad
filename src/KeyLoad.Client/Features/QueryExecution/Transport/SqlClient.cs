using System.Text.Json;
using ManagedCode.Communication;

namespace KeyLoad.Client;

public sealed partial class KeyLoadClient
{
    /// <summary>Executes one versioned SQL operation and returns its actual canonical JSON result.</summary>
    /// <param name="request">One Q1 SELECT or CALL and its bound arguments.</param>
    /// <param name="cancellationToken">Caller cancellation; CALL cancellation can leave an unknown write outcome.</param>
    /// <returns>The canonical selected result or a typed problem.</returns>
    public Task<Result<JsonElement>> ExecuteSqlAsync(SqlOperationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var possibleWrite = SqlWriteClassifier.MayWrite(request.Sql, cancellationToken, execution);
        return Send<JsonElement>(SqlOperationProtocol.Route, request, possibleWrite, null, cancellationToken);
    }
}
