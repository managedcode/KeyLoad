using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Measures the whole typed path envelope under MaxQueryBytes without retaining serialized bytes.</summary>
internal static class SqlGraphPathRequestSizer
{
    internal static void EnsureBounded(SqlGraphPathRequest request, int maximumBytes, ReadExecutionBudget budget)
    {
        budget.Check();
        if (request.Query.Parameters?.Count > SqlGraphPathSyntax.MaximumParameters)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, SqlGraphPathSyntax.ParameterBudgetDetail);
        }
        try
        {
            using var counter = new ResultByteCounterStream(maximumBytes, budget.Check);
            JsonSerializer.Serialize(counter, request, JsonDefaults.Options);
            budget.Check();
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException
            or InvalidOperationException or ArgumentException)
        {
            throw Errors.Fail(ErrorCode.Validation, SqlGraphPathSyntax.WrapperDetail);
        }
    }
}
