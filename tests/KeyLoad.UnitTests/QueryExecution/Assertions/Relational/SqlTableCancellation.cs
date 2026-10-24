namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlTableCancellation
{
    internal static async Task RequireAsync(TestDatabase database, global::KeyLoad.Query.QueryEngine engine,
        CancellationToken originalToken)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(database.Store);
        var position = database.Store.Position;
        var identity = JsonDefaults.Serialize(database.Store.Identity);
        var observed = false;
        try
        {
            _ = engine.Execute(SqlOffsetProtocol.Reader,
                new(database.Partition, SqlTablePages.TableSql, AllowFullScan: true), cancellationToken: originalToken);
        }
        catch (OperationCanceledException failure)
        {
            observed = true;
            await Assert.That(failure.CancellationToken).IsEqualTo(originalToken);
        }
        await Assert.That(observed).IsTrue();
        await SqlNullOrderingRefusals.UnchangedAsync(database.Store, image, position, identity);
    }
}
