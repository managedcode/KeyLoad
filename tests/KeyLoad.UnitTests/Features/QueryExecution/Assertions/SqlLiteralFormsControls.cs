namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlLiteralFormsControls
{
    internal static async Task InvalidAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine)
    {
        await SqlDollarStringControls.InvalidAsync(database, engine);
        await SqlRadixIntegerControls.InvalidAsync(database, engine);
        await SqlUnicodeStringControls.InvalidAsync(database, engine);
    }
}
