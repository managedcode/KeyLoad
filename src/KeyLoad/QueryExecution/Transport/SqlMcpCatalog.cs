using System.Text.Json;
using KeyLoad.Query;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Truthful execution metadata for the SQL adapter; callable targets remain canonical operations only.</summary>
internal static class SqlMcpCatalog
{
    private const string Description = "Execute one bounded selected T-SQL SELECT, supported INSERT, CREATE TABLE, or EXEC of an exact public operation. "
        + "Bind values through request parameters; mutations require the caller's original stable command identity. "
        + "Results retain the selected operation's actual JSON shape and current persisted authorization. "
        + "Supported Q1/Q2 reads retain their declared bounds; full vendor SQL is not promised. "
        + "SQL uses bounded data admission; use direct control tools when the data lane is saturated.";

    internal static McpOperationDescriptor Entry
    {
        get;
    } = new(SqlOperationProtocol.ToolName,
        SqlOperationProtocol.Route, null, null, Description,
        McpSchemaFactory.CreateInput(typeof(SqlOperationRequest), false),
        McpSchemaFactory.CreateOutput(typeof(JsonElement), true), new(false, false, true),
        null, adapter: true);

    internal static McpDecodedOperation Decode(IDictionary<string, JsonElement>? arguments, int maximumPayloadBytes, IOptions<QueryExecutionOptions> queryOptions, CancellationToken cancellationToken)
    {
        var request = McpArgumentDecoder.Request<SqlOperationRequest>(arguments, false);
        return SqlOperationCompiler.Compile(request, maximumPayloadBytes, queryOptions, cancellationToken);
    }
}
