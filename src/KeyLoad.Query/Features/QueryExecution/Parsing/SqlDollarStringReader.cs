namespace KeyLoad.Query.Features.QueryExecution;

internal static class SqlDollarStringReader
{
    internal static SqlToken Read(string sql, ref int index,
        global::KeyLoad.Core.ReadExecutionBudget? budget, int cadence)
    {
        if (!global::KeyLoad.Features.QueryExecution.SqlDollarQuoteReader.CanStart(sql.AsSpan(), index))
        { throw SqlSyntax.Invalid(); }
        var state = global::KeyLoad.Features.QueryExecution.SqlDollarQuoteReader.Start(index);
        global::KeyLoad.Features.QueryExecution.SqlDollarQuoteStatus status;
        do
        {
            budget?.Check();
            status = global::KeyLoad.Features.QueryExecution.SqlDollarQuoteReader.Read(sql.AsSpan(), ref state, cadence);
        }
        while (status == global::KeyLoad.Features.QueryExecution.SqlDollarQuoteStatus.More);
        if (status != global::KeyLoad.Features.QueryExecution.SqlDollarQuoteStatus.Complete)
        { throw SqlSyntax.Invalid(); }
        index = state.Offset;
        return new(SqlTokenKind.String, sql[state.ContentStart..state.ContentEnd]);
    }
}
