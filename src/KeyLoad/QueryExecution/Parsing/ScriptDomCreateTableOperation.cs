namespace KeyLoad.Server;

internal static class ScriptDomCreateTableOperation
{
    internal static McpDecodedOperation Compile(SqlOperationRequest request,
        global::Microsoft.SqlServer.TransactSql.ScriptDom.CreateTableStatement statement,
        Action checkBudget, CancellationToken token)
    {
        checkBudget();
        token.ThrowIfCancellationRequested();
        if (request.Partition is null || request.Parameters is { Count: > EmptyCount }
            || request.AllowFullScan || request.Cursor is not null)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        var identity = SqlOriginalMutationIdentity.Required(request.CommandId);
        var definition = ScriptDomCreateTableDefinition.Read(request.Partition, statement, checkBudget, token);
        var operation = new McpDecodedOperation(null, OperationKind.CreateTable, identity, definition);
        checkBudget();
        token.ThrowIfCancellationRequested();
        return operation;
    }

    private const int EmptyCount = 0;
}
