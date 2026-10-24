namespace KeyLoad.UnitTests.QueryExecution;

internal static class ModelJoinAliasAdmissionFlow
{
    private const string EmptyAlias = "";
    private const string ControlAlias = "\u0001";
    private const string IdentifierFailure = "An identifier is missing, too long or contains control characters.";

    internal static async Task RunAsync(global::KeyLoad.Query.QueryEngine engine,
        global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, global::KeyLoad.PartitionRef partition,
        bool events, bool repaired)
    {
        var kind = events ? SqlJoinSourceBindingFixture.Events : SqlJoinSourceBindingFixture.Queue;
        var healthy = SqlJoinSourceBindingFixture.Ast(partition, kind);
        var before = global::KeyLoad.UnitTests.Messaging.QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = global::KeyLoad.JsonDefaults.Serialize(store.Identity);
        foreach (var alias in new[] { EmptyAlias, ControlAlias })
        {
            var invalid = healthy with
            {
                Query = healthy.Query with
                {
                    Alias = alias
                }
            };
            foreach (var request in new[] { Q2CapabilityFixtures.JsonRoundTrip(invalid), Q2CapabilityFixtures.RuntimeRoundTrip(invalid) })
            {
                var execution = store.GetReadDiagnostics();
                global::KeyLoad.QueryPage? result = null;
                var failure = Assert.ThrowsExactly<global::KeyLoad.KeyLoadException>(() => result = engine.ExecuteAst(SqlJoinSourceBindingFixture.Root, request));
                await Assert.That(result).IsNull();
                await Assert.That(failure.Code).IsEqualTo(global::KeyLoad.ErrorCode.Validation);
                await Assert.That(failure.Message).IsEqualTo(IdentifierFailure);
                await Assert.That(store.GetReadDiagnostics()).IsEqualTo(execution);
                await SqlJoinSourceBindingFlow.UnchangedAsync(store, before, position, identity);
                await SqlJoinSourceBindingAssertions.PageAsync(engine.ExecuteAst(SqlJoinSourceBindingFixture.Root, healthy), kind, position, repaired);
                await SqlJoinSourceBindingAssertions.PageAsync(engine.Execute(SqlJoinSourceBindingFixture.Root,
                    SqlJoinSourceBindingFixture.Request(partition, kind)), kind, position, repaired);
                await SqlJoinSourceBindingFlow.UnchangedAsync(store, before, position, identity);
            }
        }
    }
}
