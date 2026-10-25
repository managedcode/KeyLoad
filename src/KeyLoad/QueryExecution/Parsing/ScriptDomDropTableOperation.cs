using System.Text.Json;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Server;

/// <summary>Lowers the admitted DROP shape to the sole typed public table command.</summary>
internal static class ScriptDomDropTableOperation
{
    private const int EmptyCount = 0;

    internal static McpDecodedOperation Compile(SqlOperationRequest request, Dom.DropTableStatement statement, int maximumPayloadBytes, Action checkBudget, CancellationToken token)
    {
        checkBudget();
        token.ThrowIfCancellationRequested();
        if (request.Partition is null || request.Parameters is { Count: > EmptyCount }
            || request.AllowFullScan || request.Cursor is not null)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        var identity = SqlOriginalMutationIdentity.Required(request.CommandId);
        var name = ScriptDomDropTableSyntax.Read(statement, checkBudget, token);
        var table = new DropTableRequest(request.Partition.TenantId, request.Partition.DatabaseId, name);
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [McpCatalogProtocol.Request] = JsonSerializer.SerializeToElement(table, JsonDefaults.Options),
            [McpCatalogProtocol.CommandId] = JsonSerializer.SerializeToElement(identity, JsonDefaults.Options)
        };
        var operation = McpArgumentDecoder.HeaderCommand<DropTableRequest>(arguments,
            OperationKind.DropTable, maximumPayloadBytes);
        checkBudget();
        token.ThrowIfCancellationRequested();
        return operation;
    }
}
