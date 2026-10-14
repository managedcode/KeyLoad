namespace KeyLoad.Client;

/// <summary>Bounds local prefix inspection and preserves unknown write outcomes.</summary>
internal static class SqlWriteClassifier
{
    internal static bool MayWrite(string? sql, KeyLoadClientExecutionOptions bounds, CancellationToken cancellationToken)
    {
        if (sql is null)
        { return true; }
        var limits = KeyLoadClientExecutionOptions.SqlInspection(bounds);
        if (cancellationToken.IsCancellationRequested)
        { return NativeCancelledSqlInspection.MayWrite(sql, bounds.SqlBudgetCheckInterval, limits); }
        try
        {
            var syntax = global::KeyLoad.Features.QueryExecution.ScriptDomSyntax.Parse(sql, limits,
                cancellationToken.ThrowIfCancellationRequested, cancellationToken);
            return global::KeyLoad.Features.QueryExecution.ScriptDomSyntax.MayWrite(syntax);
        }
        catch (KeyLoadException)
        { return true; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { return true; }
    }
}
