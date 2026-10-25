using KeyLoad.Core;
using KeyLoad.QueryExecution;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Query.QueryExecution;

internal static class ScriptDomQueryAdapter
{
    internal static AstQueryRequest Lower(Dom.TSqlStatement statement, QueryRequest request,
        global::Microsoft.Extensions.Options.IOptions<QueryExecutionOptions> executionOptions, ReadExecutionBudget budget, CancellationToken cancellationToken)
        => Lower(statement, request, executionOptions, budget, null, cancellationToken);

    internal static AstQueryRequest Lower(Dom.TSqlStatement statement, QueryRequest request,
        global::Microsoft.Extensions.Options.IOptions<QueryExecutionOptions> executionOptions, ReadExecutionBudget budget, int? positionalParameterCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(executionOptions);
        var execution = executionOptions.Value;
        execution.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        budget.Check();
        if (request.QueryDialectVersion != SqlOperationProtocol.DefaultQueryDialectVersion)
        {
            throw ScriptDomErrors.Unsupported();
        }
        if (request.Parameters?.Count > execution.MaximumParameters || positionalParameterCount is < EmptyCount
            || positionalParameterCount > execution.MaximumParameters)
        {
            throw ScriptDomErrors.Invalid();
        }
        var context = new ScriptDomLoweringContext(executionOptions, budget,
            request.Parameters, positionalParameterCount);
        var query = ScriptDomSelectLowering.Read(statement, request.QueryDialectVersion, context);
        cancellationToken.ThrowIfCancellationRequested();
        budget.Check();
        return new(request.Partition, query, request.Parameters, request.AllowFullScan, request.Cursor,
            AstVersion: request.QueryDialectVersion);
    }

    private const int EmptyCount = 0;
}
