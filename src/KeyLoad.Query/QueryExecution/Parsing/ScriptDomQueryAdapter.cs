using KeyLoad.Core;
using KeyLoad.Features.QueryExecution;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class ScriptDomQueryAdapter
{
    internal static AstQueryRequest Lower(Dom.TSqlStatement statement, QueryRequest request, global::Microsoft.Extensions.Options.IOptions<DatabaseLimits> limitsOptions,
        global::Microsoft.Extensions.Options.IOptions<QueryExecutionOptions> executionOptions, ReadExecutionBudget budget, CancellationToken cancellationToken)
        => Lower(statement, request, limitsOptions, executionOptions, budget, null, cancellationToken);

    internal static AstQueryRequest Lower(Dom.TSqlStatement statement, QueryRequest request, global::Microsoft.Extensions.Options.IOptions<DatabaseLimits> limitsOptions,
        global::Microsoft.Extensions.Options.IOptions<QueryExecutionOptions> executionOptions, ReadExecutionBudget budget, int? positionalParameterCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(limitsOptions);
        ArgumentNullException.ThrowIfNull(executionOptions);
        var limits = limitsOptions.Value;
        var execution = executionOptions.Value;
        limits.Validate();
        execution.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        budget.Check();
        if (request.QueryDialectVersion is not (NamedVersion or JoinVersion))
        {
            throw ScriptDomErrors.Unsupported();
        }
        if (request.Parameters?.Count > execution.MaximumParameters || positionalParameterCount is < EmptyCount
            || positionalParameterCount > execution.MaximumParameters)
        {
            throw ScriptDomErrors.Invalid();
        }
        var context = new ScriptDomLoweringContext(limitsOptions, executionOptions, budget, limits.MaxQueryTokens,
            request.Parameters, positionalParameterCount);
        var query = ScriptDomSelectLowering.Read(statement, request.QueryDialectVersion, context);
        cancellationToken.ThrowIfCancellationRequested();
        budget.Check();
        return new(request.Partition, query, request.Parameters, request.AllowFullScan, request.Cursor,
            AstVersion: request.QueryDialectVersion);
    }
    private const int NamedVersion = 1;
    private const int JoinVersion = 2;

    private const int EmptyCount = 0;
}
