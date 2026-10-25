using System.Text.Json;

namespace KeyLoad.Query.QueryExecution;

/// <summary>Checks a typed SQL graph request parameter count and serializability without retaining serialized bytes.</summary>
internal static class SqlGraphSearchRequestSizer
{
    internal static void EnsureBounded(SqlGraphSearchRequest request, int maximumParameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Query is null)
        {
            throw Errors.Fail(ErrorCode.Validation, SqlGraphSearchSyntax.WrapperDetail);
        }
        if (request.Query.Parameters?.Count > maximumParameters)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, SqlGraphSearchSyntax.ParameterBudgetDetail);
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            JsonSerializer.Serialize(Stream.Null, request, JsonDefaults.Options);
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Validation, SqlGraphSearchSyntax.WrapperDetail);
        }
        catch (InvalidOperationException)
        {
            throw Errors.Fail(ErrorCode.Validation, SqlGraphSearchSyntax.WrapperDetail);
        }
    }
}
