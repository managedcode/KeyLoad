using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query;

/// <summary>The current bounded SQL parser. Unsupported syntax is rejected, never passed through to storage.</summary>
public sealed class SqlParser
{
    private readonly Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement statement;
    private readonly global::KeyLoad.Query.QueryExecution.ScriptDomLoweringContext context;

    /// <summary>Creates the execution T-SQL adapter with centrally validated query limits.</summary>
    /// <param name="sql">Original bounded T-SQL text.</param>
    /// <param name="executionOptions">Original query execution limits.</param>
    public SqlParser(string sql, IOptions<QueryExecutionOptions> executionOptions)
        : this(sql, executionOptions, null) { }

    internal SqlParser(string sql, IOptions<QueryExecutionOptions> executionOptions,
        ReadExecutionBudget? budget)
    {
        ArgumentNullException.ThrowIfNull(sql) /* KLD0041: BCL null validation accepts the sql string reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(executionOptions) /* KLD0041: BCL null validation accepts the executionOptions IOptions reference through its object parameter. */;
        var execution = executionOptions.Value;
        execution.Validate();
        var originalBudget = budget ?? new ReadExecutionBudget();
        statement = global::KeyLoad.QueryExecution.ScriptDomSyntax.Parse(sql, originalBudget.Check, originalBudget.Cancellation);
        context = new(executionOptions, originalBudget);
    }

    /// <summary>Lowers supported execution T-SQL into the existing immutable authorized query plan.</summary>
    /// <returns>The canonical typed SELECT plan.</returns>
    public SelectQuery Parse()
        => global::KeyLoad.Query.QueryExecution.ScriptDomSelectLowering.Read(statement, context);
}
