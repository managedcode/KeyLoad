using System.Text.Json;
using KeyLoad.Query;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Bounds text, complete serialized ingress and parameter structure before typed target decode.</summary>
internal static class SqlOperationBounds
{
    internal static void Validate(SqlOperationRequest request, int maximumPayloadBytes,
        IOptions<QueryExecutionOptions> queryOptions, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(queryOptions);
        queryOptions.Value.Validate();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPayloadBytes);
        if (request is null || string.IsNullOrEmpty(request.Sql))
        {
            throw SqlOperationSyntax.InvalidInput();
        }
        if (request.Version != SqlOperationProtocol.Version)
        {
            throw SqlOperationSyntax.UnsupportedInput();
        }
        if (string.IsNullOrWhiteSpace(request.Sql))
        {
            throw SqlOperationSyntax.InvalidInput();
        }
        if (request.Parameters?.Count > queryOptions.Value.MaximumParameters)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, SqlOperationSyntax.StructureExceeded);
        }
        using var counter = new SqlOperationByteCounter(maximumPayloadBytes, cancellationToken);
        try
        {
            JsonSerializer.Serialize(counter, request, JsonDefaults.Options);
        }
        catch (JsonException)
        {
            throw SqlOperationSyntax.InvalidInput();
        }
        var structure = new SqlOperationParameterBounds(cancellationToken);
        IEnumerable<JsonElement> values = request.Parameters is { } parameters ? parameters.Values : [];
        foreach (var parameter in values)
        {
            structure.Visit(parameter);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
}
