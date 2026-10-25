using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.Query.QueryExecution;

/// <summary>Checks the typed path envelope parameter count and serializability without retaining serialized bytes.</summary>
internal static class SqlGraphPathRequestSizer
{
    internal static void EnsureBounded(SqlGraphPathRequest request, int maximumParameters, ReadExecutionBudget budget)
    {
        budget.Check();
        if (request.Query.Parameters?.Count > maximumParameters)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, SqlGraphPathSyntax.ParameterBudgetDetail);
        }
        try
        {
            JsonSerializer.Serialize(Stream.Null, request, JsonDefaults.Options);
            budget.Check();
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException
            or InvalidOperationException or ArgumentException)
        {
            throw Errors.Fail(ErrorCode.Validation, SqlGraphPathSyntax.WrapperDetail);
        }
    }
}
