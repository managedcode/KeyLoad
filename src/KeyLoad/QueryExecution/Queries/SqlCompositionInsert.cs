namespace KeyLoad.Server;

/// <summary>Compiles the closed model INSERT grammar into the existing atomic command.</summary>
internal static class SqlCompositionInsert
{
    internal static CommandRequest Compile(SqlOperationRequest request,
        global::Microsoft.SqlServer.TransactSql.ScriptDom.InsertStatement statement,
        int? positionalParameterCount, Action checkBudget, CancellationToken token)
    {
        checkBudget();
        token.ThrowIfCancellationRequested();
        if (request.AllowFullScan || request.Cursor is not null || request.Partition is null)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        if (request.QueryDialectVersion != SqlOperationProtocol.DefaultQueryDialectVersion)
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        var (target, query) = ScriptDomInsertShape.Read(statement);
        var parameters = new ScriptDomCompositionParameters(request.Parameters, positionalParameterCount, checkBudget, token);
        var (id, mutation) = target.Equals(ScriptDomInsertShape.GraphEdges, StringComparison.OrdinalIgnoreCase)
            ? ScriptDomQueueToGraphInsert.Read(query, parameters)
            : target.Equals(ScriptDomInsertShape.QueueMessages, StringComparison.OrdinalIgnoreCase)
                ? ScriptDomGraphToQueueInsert.Read(query, parameters, request.QueryDialectVersion)
                : throw ScriptDomOperationErrors.Unsupported();
        if (id == Guid.Empty)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        parameters.Complete();
        checkBudget();
        token.ThrowIfCancellationRequested();
        return new(id, request.Partition, [mutation]);
    }
}
