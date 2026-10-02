using System.Text;
using System.Text.Json;

namespace KeyLoad.Server;

/// <summary>Bounds text, complete serialized ingress and parameter structure before typed target decode.</summary>
internal static class SqlOperationBounds
{
    internal static void Validate(SqlOperationRequest request, DatabaseLimits limits, int maximumPayloadBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPayloadBytes);
        if (request is null || string.IsNullOrEmpty(request.Sql))
        { throw SqlOperationSyntax.InvalidInput(); }
        if (request.Version != SqlOperationProtocol.Version)
        { throw SqlOperationSyntax.UnsupportedInput(); }
        if (request.Sql.Length > limits.MaxQueryBytes || Encoding.UTF8.GetByteCount(request.Sql) > limits.MaxQueryBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, SqlOperationSyntax.TextExceeded); }
        if (string.IsNullOrWhiteSpace(request.Sql))
        { throw SqlOperationSyntax.InvalidInput(); }
        if (request.Parameters?.Count > SqlOperationSyntax.MaximumParameters)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, SqlOperationSyntax.StructureExceeded); }
        using var counter = new SqlOperationByteCounter(maximumPayloadBytes, cancellationToken);
        try
        { JsonSerializer.Serialize(counter, request, JsonDefaults.Options); }
        catch (JsonException)
        { throw SqlOperationSyntax.InvalidInput(); }
        var structure = new SqlOperationParameterBounds(limits, cancellationToken);
        IEnumerable<JsonElement> values = request.Parameters is { } parameters ? parameters.Values : [];
        foreach (var parameter in values)
        { structure.Visit(parameter); }
        cancellationToken.ThrowIfCancellationRequested();
    }
}
