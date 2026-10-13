namespace KeyLoad.Query.Features.QueryExecution;

internal static class SqlStringContinuation
{
    internal static bool Consume(string sql, ref int offset,
        global::KeyLoad.Core.ReadExecutionBudget? budget, int cadence)
    {
        var next = offset;
        var state = default(global::KeyLoad.Features.QueryExecution.SqlStringContinuationState);
        bool? continued;
        do
        {
            budget?.Check();
            continued = global::KeyLoad.Features.QueryExecution.SqlStringContinuationReader.Read(
                sql.AsSpan(), ref next, ref state, cadence);
        } while (!continued.HasValue);
        budget?.Check();
        if (continued != true)
        { return false; }
        offset = next;
        return true;
    }
}
