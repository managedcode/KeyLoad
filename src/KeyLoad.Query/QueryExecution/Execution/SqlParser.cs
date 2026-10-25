using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query;

/// <summary>A bounded Q1/Q2 parser. Unsupported syntax is rejected, never passed through to storage.</summary>
public sealed class SqlParser
{
    private const int DefaultDialectVersion = SqlOperationProtocol.DefaultQueryDialectVersion;
    private readonly Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement statement;
    private readonly global::KeyLoad.Query.QueryExecution.ScriptDomLoweringContext context;
    private readonly int dialectVersion;

    /// <summary>Creates the execution T-SQL adapter with centrally validated query limits.</summary>
    /// <param name="sql">Original bounded T-SQL text.</param>
    /// <param name="executionOptions">Original query execution limits.</param>
    public SqlParser(string sql, IOptions<QueryExecutionOptions> executionOptions)
        : this(sql, executionOptions, null, DefaultDialectVersion) { }

    internal SqlParser(string sql, IOptions<QueryExecutionOptions> executionOptions,
        ReadExecutionBudget? budget, int dialectVersion = DefaultDialectVersion)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(executionOptions);
        var execution = executionOptions.Value;
        execution.Validate();
        if (dialectVersion != DefaultDialectVersion)
        {
            throw global::KeyLoad.QueryExecution.ScriptDomErrors.Unsupported();
        }
        this.dialectVersion = dialectVersion;
        var originalBudget = budget ?? new ReadExecutionBudget();
        statement = global::KeyLoad.QueryExecution.ScriptDomSyntax.Parse(sql, originalBudget.Check, originalBudget.Cancellation);
        context = new(executionOptions, originalBudget);
    }

    /// <summary>Lowers supported execution T-SQL into the existing immutable authorized query plan.</summary>
    /// <returns>The canonical typed SELECT plan.</returns>
    public SelectQuery Parse()
        => global::KeyLoad.Query.QueryExecution.ScriptDomSelectLowering.Read(statement, dialectVersion, context);
}
