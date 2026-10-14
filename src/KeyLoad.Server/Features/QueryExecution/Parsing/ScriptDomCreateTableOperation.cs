namespace KeyLoad.Server;

internal static class ScriptDomCreateTableOperation
{
    internal static McpDecodedOperation Compile(SqlOperationRequest request,
        global::Microsoft.SqlServer.TransactSql.ScriptDom.CreateTableStatement statement,
        global::Microsoft.Extensions.Options.IOptions<DatabaseLimits> limits,
        int maximumPayloadBytes, Action checkBudget, CancellationToken token)
    {
        checkBudget();
        token.ThrowIfCancellationRequested();
        if (request.Partition is null || request.Parameters is { Count: > EmptyCount }
            || request.AllowFullScan || request.Cursor is not null)
        { throw ScriptDomOperationErrors.Invalid(); }
        var identity = SqlOriginalMutationIdentity.Required(request.CommandId);
        var definition = ScriptDomCreateTableDefinition.Read(request.Partition, statement, limits, checkBudget, token);
        var arguments = new Dictionary<string, global::System.Text.Json.JsonElement>(StringComparer.Ordinal)
        {
            [McpCatalogProtocol.Request] = global::System.Text.Json.JsonSerializer.SerializeToElement(definition, JsonDefaults.Options),
            [McpCatalogProtocol.CommandId] = global::System.Text.Json.JsonSerializer.SerializeToElement(identity, JsonDefaults.Options)
        };
        var operation = McpArgumentDecoder.HeaderCommand<CreateTableRequest>(arguments, OperationKind.CreateTable, maximumPayloadBytes);
        checkBudget();
        token.ThrowIfCancellationRequested();
        return operation;
    }

    private const int EmptyCount = 0;
}
