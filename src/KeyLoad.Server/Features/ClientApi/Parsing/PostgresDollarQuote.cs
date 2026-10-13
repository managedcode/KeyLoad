namespace KeyLoad.Server;

internal static class PostgresDollarQuote
{
    internal static void Copy(string sql, ref int offset, global::System.Text.StringBuilder output,
        int cadence, CancellationToken token)
    {
        var start = offset;
        var state = global::KeyLoad.Features.QueryExecution.SqlDollarQuoteReader.Start(offset);
        global::KeyLoad.Features.QueryExecution.SqlDollarQuoteStatus status;
        do
        {
            token.ThrowIfCancellationRequested();
            status = global::KeyLoad.Features.QueryExecution.SqlDollarQuoteReader.Read(sql.AsSpan(), ref state, cadence);
        }
        while (status == global::KeyLoad.Features.QueryExecution.SqlDollarQuoteStatus.More);
        if (status != global::KeyLoad.Features.QueryExecution.SqlDollarQuoteStatus.Complete)
        { throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid); }
        offset = state.Offset;
        output.Append(sql, start, offset - start);
    }
}
