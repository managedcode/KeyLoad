using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlStringContinuationControls
{
    private const string SameLine = "label = 'lo' 'w'";
    private const string BlockSeparator = "label = 'lo' /*\n*/ 'w'";
    private const string PrefixedNext = "label = 'lo'\nE'w'";
    private const string OpenContinuation = "label = 'lo'\n'unterminated";
    private const string InvalidBytes = "label = E'\\xc3'\n'plain'";

    internal static async Task InvalidAsync(TestDatabase database, QueryEngine engine)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        foreach (var (clause, code) in new[]
        {
            (SameLine, ErrorCode.Validation), (BlockSeparator, ErrorCode.Validation),
            (PrefixedNext, ErrorCode.Validation), (OpenContinuation, ErrorCode.Validation),
            (InvalidBytes, ErrorCode.Validation)
        })
        {
            var request = new QueryRequest(database.Partition,
                SqlNullDistinctCases.Prefix + clause + SqlNullDistinctCases.Tail, AllowFullScan: true);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(
                SqlNullOrderingProtocol.Reader, request)).Code).IsEqualTo(code);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
            await SqlNullDistinctPages.HealthyAsync(database.Database, database.Store,
                database.Partition, SqlNullDistinctRefusals.InitialRevision);
            await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
        }
    }
}
