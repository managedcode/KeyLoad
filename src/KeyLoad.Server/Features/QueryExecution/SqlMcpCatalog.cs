using System.Text.Json;

namespace KeyLoad.Server;

/// <summary>Truthful native metadata for the SQL adapter; callable targets remain canonical operations only.</summary>
internal static class SqlMcpCatalog
{
    private const string Description = "Execute one version-one Q1 SELECT or CALL exact_public_operation(@arguments). " +
        "CALL binds the canonical argument envelope, including caller stable command IDs for mutations. " +
        "Results have the selected operation's actual JSON shape. SQL uses bounded data admission; " +
        "use direct control tools when the data lane is saturated. Joins and full vendor SQL are unsupported.";

    internal static McpOperationDescriptor Entry { get; } = new(SqlOperationProtocol.ToolName,
        SqlOperationProtocol.Route, null, null, Description,
        McpSchemaFactory.CreateInput(typeof(SqlOperationRequest), false),
        McpSchemaFactory.CreateOutput(typeof(JsonElement), true), new(false, false, true),
        Decode, adapter: true);

    internal static McpDecodedOperation Decode(IDictionary<string, JsonElement>? arguments, int maximumPayloadBytes)
        => Decode(arguments, maximumPayloadBytes, new DatabaseLimits(), CancellationToken.None);

    internal static McpDecodedOperation Decode(IDictionary<string, JsonElement>? arguments, int maximumPayloadBytes,
        DatabaseLimits limits, CancellationToken cancellationToken)
    {
        var request = McpArgumentDecoder.Request<SqlOperationRequest>(arguments, false);
        return SqlOperationCompiler.Compile(request, limits, maximumPayloadBytes, cancellationToken);
    }
}
