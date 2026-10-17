using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlBooleanTruthPages
{
    internal static async Task HealthyAsync(global::KeyLoad.Core.DatabaseEngine owner,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, PartitionRef partition, long lowRevision)
    {
        var engine = new QueryEngine(owner, UnitExecutionOptions.QueryExecution());
        await Assert.That(engine.Capabilities.ReadProfiles.Contains(SqlBooleanTruthCases.Profile)).IsTrue();
        await Assert.That(engine.Capabilities.ReadProfiles.Contains(SqlBooleanTruthCases.OperandProfile)).IsFalse();
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        foreach (var item in SqlBooleanTruthCases.All)
        {
            var expected = new QueryPage([.. item.Ids.Select(id => new QueryRow(id,
                id == "low" ? lowRevision : SqlBooleanTruthRefusals.InitialRevision,
                "{\"label\":\"" + id + "\"}", RedactedFields: []))], null, position, "bounded-full-scan");
            await SqlNullOrderingPages.EqualAsync(expected, engine.Execute(SqlNullOrderingProtocol.Reader,
                new(partition, SqlBooleanTruthCases.Prefix + item.Clause + SqlBooleanTruthCases.Tail, SqlBooleanTruthCases.Parameters(), AllowFullScan: true)));
            await SqlNullOrderingPages.EqualAsync(expected, engine.ExecuteAst(SqlNullOrderingProtocol.Reader,
                SqlBooleanTruthCases.Ast(partition, item)));
            await SqlNullOrderingRefusals.UnchangedAsync(store, image, position, identity);
        }
        await SqlBooleanOperandControls.ParametersAsync(engine, store, partition, lowRevision);
    }
}
