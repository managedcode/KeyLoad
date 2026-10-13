using System.Text;

namespace KeyLoad.Server;

internal static class PostgresStringContinuation
{
    internal static bool Consume(string sql, ref int offset, StringBuilder output,
        int cadence, CancellationToken token)
    {
        var next = offset;
        var state = default(global::KeyLoad.Features.QueryExecution.SqlStringContinuationState);
        bool? continued;
        do
        {
            token.ThrowIfCancellationRequested();
            continued = global::KeyLoad.Features.QueryExecution.SqlStringContinuationReader.Read(
                sql.AsSpan(), ref next, ref state, cadence);
        } while (!continued.HasValue);
        token.ThrowIfCancellationRequested();
        if (continued != true)
        { return false; }
        output.Append(sql, offset, next - offset);
        offset = next;
        return true;
    }
}
