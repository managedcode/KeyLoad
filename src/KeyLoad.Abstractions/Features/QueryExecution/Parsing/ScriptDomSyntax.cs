using System.Text;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Features.QueryExecution;

internal static class ScriptDomSyntax
{
    /// <summary>Parses exactly one native T-SQL statement inside the existing operation lifetime.</summary>
    /// <param name="sql">Original bounded SQL text.</param>
    /// <param name="limitsOptions">Validated operation limits.</param>
    /// <param name="checkBudget">Original operation deadline and work callback.</param>
    /// <param name="cancellationToken">Original caller token.</param>
    /// <returns>The native statement; successful parsing does not authorize its execution.</returns>
    internal static Dom.TSqlStatement Parse(string sql, global::Microsoft.Extensions.Options.IOptions<DatabaseLimits> limitsOptions, Action checkBudget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(limitsOptions);
        var limits = limitsOptions.Value;
        ArgumentNullException.ThrowIfNull(checkBudget);
        void Check()
        { cancellationToken.ThrowIfCancellationRequested(); checkBudget(); }
        limits.Validate();
        Check();
        if (sql.Length > limits.MaxQueryBytes || Encoding.UTF8.GetByteCount(sql) > limits.MaxQueryBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, ScriptDomErrors.BytesDetail); }
        var parser = new Dom.TSql180Parser(initialQuotedIdentifiers: true, Dom.SqlEngineType.Standalone);
        using var reader = new ScriptDomBoundedReader(sql, Check);
        var tokens = parser.GetTokenStream(reader, out var lexicalErrors);
        Check();
        if (lexicalErrors.Count != EmptyCount)
        { throw ScriptDomErrors.Invalid(); }
        ScriptDomTokenBounds.Require(tokens, limits, Check);
        var fragment = parser.Parse(new ScriptDomCheckedTokens(tokens, limitsOptions, Check), out var errors);
        Check();
        if (errors.Count != EmptyCount)
        { throw ScriptDomErrors.Invalid(); }
        if (fragment is not Dom.TSqlScript script)
        { throw ScriptDomErrors.Unsupported(); }
        if (script.TrailingGoCount != EmptyCount)
        { throw ScriptDomErrors.Unsupported(); }
        if (script.Batches.Count == EmptyCount
            || script.Batches.Count == SingleElement && script.Batches[FirstIndex].Statements.Count == EmptyCount)
        { throw ScriptDomErrors.Invalid(); }
        if (script.Batches.Count != SingleElement || script.Batches[FirstIndex].Statements.Count != SingleElement)
        { throw ScriptDomErrors.Unsupported(); }
        var statement = script.Batches[FirstIndex].Statements[FirstIndex];
        statement.Accept(new ScriptDomSlotVisitor(RequireOrigin(statement), null, Check, cancellationToken));
        Check();
        return statement;
    }
    internal static void ValidateNativeSessionSlots(Dom.TSqlStatement statement, int positionalParameterCount,
        Action checkBudget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(checkBudget);
        if (positionalParameterCount < EmptyCount)
        { throw ScriptDomErrors.Invalid(); }
        cancellationToken.ThrowIfCancellationRequested();
        checkBudget();
        statement.Accept(new ScriptDomSlotVisitor(RequireOrigin(statement), positionalParameterCount, checkBudget, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        checkBudget();
    }
    internal static bool TryGetNativeSessionSlot(Dom.MoneyLiteral literal, int positionalParameterCount, out string canonicalName)
        => ScriptDomNativeSlots.TryGet(literal, positionalParameterCount, out canonicalName);
    private static ScriptDomCheckedTokens RequireOrigin(Dom.TSqlStatement statement)
        => statement.ScriptTokenStream as ScriptDomCheckedTokens ?? throw ScriptDomErrors.Unsupported();
    internal static bool IsReadOnlySelect(Dom.TSqlStatement statement)
        => statement is Dom.SelectStatement { Into: null };
    internal static bool HasQueryRows(Dom.TSqlStatement statement) => IsReadOnlySelect(statement);
    internal static bool MayWrite(Dom.TSqlStatement statement) => !IsReadOnlySelect(statement);
    private const int EmptyCount = 0;

    private const int FirstIndex = 0;
    private const int SingleElement = 1;
}
