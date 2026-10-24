using KeyLoad.Query;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlBetweenSymmetricPages
{
    internal static async Task HealthyAsync(global::KeyLoad.Core.DatabaseEngine owner,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, PartitionRef partition, long lowRevision)
    {
        var engine = new QueryEngine(owner, UnitExecutionOptions.QueryExecution());
        await Assert.That(engine.Capabilities.ReadProfiles.Contains(SqlBetweenSymmetricCases.Profile)).IsFalse();
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        foreach (var item in SqlBetweenSymmetricCases.All)
        {
            var expected = new QueryPage([.. item.Ids.Select(id => new QueryRow(id,
                id == "low" ? lowRevision : SqlBetweenSymmetricRefusals.InitialRevision,
                "{\"label\":\"" + id + "\"}", RedactedFields: []))], null, position, "bounded-full-scan");
            await SqlNullOrderingPages.EqualAsync(expected, engine.Execute(SqlNullOrderingProtocol.Reader,
                new(partition, SqlBetweenSymmetricCases.Prefix + item.Clause + SqlBetweenSymmetricCases.Tail, AllowFullScan: true)));
            await SqlNullOrderingPages.EqualAsync(expected, engine.ExecuteAst(SqlNullOrderingProtocol.Reader,
                SqlBetweenSymmetricCases.Ast(partition, item)));
            await SqlNullOrderingRefusals.UnchangedAsync(store, image, position, identity);
        }
    }
}
