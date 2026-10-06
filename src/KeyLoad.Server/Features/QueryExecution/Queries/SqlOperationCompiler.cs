using KeyLoad.Query;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Compiles one admitted SQL envelope to the sole canonical operation decoder, without execution.</summary>
internal static class SqlOperationCompiler
{
    /// <summary>Preserves the selected operation's authority boundary, payload and stable caller identity.</summary>
    internal static McpDecodedOperation Compile(SqlOperationRequest request, IOptions<DatabaseLimits> limitsOptions,
        int maximumPayloadBytes, IOptions<QueryExecutionOptions> queryOptions, CancellationToken cancellationToken = default)
    {
        SqlOperationBounds.Validate(request, limitsOptions, maximumPayloadBytes, queryOptions, cancellationToken);
        var limits = limitsOptions.Value;
        var reader = new SqlOperationSyntaxReader(request.Sql, limits.MaxQueryTokens, limits.MaxQueryDepth, queryOptions, cancellationToken);
        var statement = ReadStatement(reader);
        if (statement.Equals(SqlOperationSyntax.Select, StringComparison.OrdinalIgnoreCase)
            || statement.Equals(SqlOperationSyntax.Explain, StringComparison.OrdinalIgnoreCase))
        { return Select(request, maximumPayloadBytes, cancellationToken); }
        if (!statement.Equals(SqlOperationSyntax.Call, StringComparison.OrdinalIgnoreCase))
        { throw SqlOperationSyntax.UnsupportedInput(); }
        if (request.AllowFullScan || request.Cursor is not null)
        { throw SqlOperationSyntax.InvalidInput(); }
        var name = reader.Identifier();
        reader.Need(SqlOperationSyntax.OpenParenthesis);
        reader.Need(SqlOperationSyntax.ParameterPrefix);
        var parameter = reader.Identifier();
        reader.Need(SqlOperationSyntax.CloseParenthesis);
        reader.Complete();
        var descriptor = Target(name);
        var arguments = BindArguments(request.Parameters, parameter);
        cancellationToken.ThrowIfCancellationRequested();
        var operation = descriptor.Decode(arguments, maximumPayloadBytes);
        cancellationToken.ThrowIfCancellationRequested();
        return operation;
    }

    private static string ReadStatement(SqlOperationSyntaxReader reader)
    {
        reader.SkipTrivia();
        try
        { return reader.Identifier(); }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Validation)
        { throw SqlOperationSyntax.UnsupportedInput(); }
    }

    private static McpDecodedOperation Select(SqlOperationRequest request, int maximumPayloadBytes,
        CancellationToken cancellationToken)
    {
        if (request.Partition is null)
        { throw SqlOperationSyntax.InvalidInput(); }
        var query = new QueryRequest(request.Partition, request.Sql, request.Parameters, request.AllowFullScan, request.Cursor);
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        { [McpCatalogProtocol.Request] = JsonSerializer.SerializeToElement(query, JsonDefaults.Options) };
        cancellationToken.ThrowIfCancellationRequested();
        var operation = Target(McpToolNames.QueryExecute).Decode(arguments, maximumPayloadBytes);
        cancellationToken.ThrowIfCancellationRequested();
        return operation;
    }

    private static McpOperationDescriptor Target(string name)
    {
        if (name == SqlOperationProtocol.ToolName || !McpOperationCatalog.TryGet(name, out var descriptor)
            || descriptor.ReadKind.HasValue == descriptor.CommandKind.HasValue)
        { throw SqlOperationSyntax.UnsupportedInput(); }
        return descriptor;
    }

    private static Dictionary<string, JsonElement> BindArguments(Dictionary<string, JsonElement>? parameters,
        string parameter)
    {
        if (parameters is not { Count: SqlOperationSyntax.CallParameterCount }
            || !parameters.TryGetValue(parameter, out var envelope) || envelope.ValueKind != JsonValueKind.Object
            || parameters.Keys.Any(name => !StringComparer.Ordinal.Equals(name, parameter)))
        { throw SqlOperationSyntax.InvalidInput(); }
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in envelope.EnumerateObject())
        {
            if (!arguments.TryAdd(property.Name, property.Value))
            { throw SqlOperationSyntax.InvalidInput(); }
        }
        return arguments;
    }
}
