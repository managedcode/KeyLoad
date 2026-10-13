using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlNullDistinctPages
{
    internal static async Task HealthyAsync(global::KeyLoad.Core.DatabaseEngine owner,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, PartitionRef partition, long lowRevision)
    {
        var engine = new QueryEngine(owner, UnitExecutionOptions.QueryExecution());
        await Assert.That(engine.Capabilities.ReadProfiles.Contains(SqlNullDistinctCases.Profile)).IsTrue();
        await Assert.That(engine.Capabilities.ReadProfiles.Contains(SqlNullDistinctCases.NullSynonymProfile)).IsTrue();
        await Assert.That(engine.Capabilities.ReadProfiles.Contains(SqlNullDistinctCases.PositiveProfile)).IsTrue();
        await Assert.That(engine.Capabilities.ReadProfiles.Contains(SqlNullDistinctCases.LeadingProfile)).IsTrue();
        await Assert.That(engine.Capabilities.ReadProfiles.Contains(SqlNullDistinctCases.HexProfile)).IsTrue();
        await Assert.That(engine.Capabilities.ReadProfiles.Contains(SqlNullDistinctCases.EscapeProfile)).IsTrue();
        await Assert.That(engine.Capabilities.ReadProfiles.Contains(SqlNullDistinctCases.DollarProfile)).IsTrue();
        await Assert.That(engine.Capabilities.ReadProfiles.Contains(SqlNullDistinctCases.BinaryOctalProfile)).IsTrue();
        await Assert.That(engine.Capabilities.ReadProfiles.Contains(SqlNullDistinctCases.UnicodeProfile)).IsTrue();
        await Assert.That(engine.Capabilities.ReadProfiles.Contains(SqlNullDistinctCases.NumericSeparatorProfile)).IsTrue();
        await Assert.That(engine.Capabilities.ReadProfiles.Contains(SqlUnicodeIdentifierCases.Profile)).IsTrue();
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        foreach (var item in SqlNullDistinctCases.All)
        {
            var expected = new QueryPage([.. item.Ids.Select(id => new QueryRow(id,
                id == "low" ? lowRevision : SqlNullDistinctRefusals.InitialRevision,
                "{\"label\":\"" + id + "\"}", RedactedFields: []))], null, position, "bounded-full-scan");
            await SqlNullOrderingPages.EqualAsync(expected, engine.Execute(SqlNullOrderingProtocol.Reader,
                new(partition, SqlNullDistinctCases.Prefix + item.Clause + SqlNullDistinctCases.Tail, AllowFullScan: true)));
            await SqlNullOrderingPages.EqualAsync(expected, engine.ExecuteAst(SqlNullOrderingProtocol.Reader,
                SqlNullDistinctCases.Ast(partition, item)));
            await SqlNullOrderingRefusals.UnchangedAsync(store, image, position, identity);
        }
        await SqlNumericSeparatorControls.ParametersAsync(engine, store, partition, lowRevision);
        await SqlUnicodeIdentifierControls.ParametersAsync(engine, store, partition, lowRevision);
    }
}
