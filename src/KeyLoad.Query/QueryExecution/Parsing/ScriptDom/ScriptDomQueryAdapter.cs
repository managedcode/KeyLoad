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
        ArgumentNullException.ThrowIfNull(statement) /* KLD0041: BCL null validation accepts the statement TSqlStatement reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: BCL null validation accepts the request QueryRequest reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: BCL null validation accepts the budget ReadExecutionBudget reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(executionOptions) /* KLD0041: BCL null validation accepts the executionOptions IOptions reference through its object parameter. */;
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
