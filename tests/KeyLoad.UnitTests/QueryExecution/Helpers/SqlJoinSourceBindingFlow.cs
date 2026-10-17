namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlJoinSourceBindingFlow
{
    private const int FirstSelection = 0;
    private const string TypedFailure = "The bounded relational join is invalid.";
    private const string QueueFailure = "The queue entity-reference join is unsupported.";
    private const string EventFailure = "The event entity-reference join is unsupported.";

    internal static async Task RunAsync(global::KeyLoad.Core.DatabaseEngine owner, global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store,
        global::KeyLoad.PartitionRef partition, string kind, global::KeyLoad.CommandRequest command, global::KeyLoad.CommitReceipt receipt)
    {
        var engine = new global::KeyLoad.Query.QueryEngine(owner, UnitExecutionOptions.QueryExecution());
        var before = global::KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = global::KeyLoad.JsonDefaults.Serialize(store.Identity);
        var healthy = SqlJoinSourceBindingFixture.Ast(partition, kind);
        var invalid = healthy with
        {
            Query = healthy.Query with
            {
                Projection = [healthy.Query.Projection[FirstSelection] with { SourceAlias = SqlJoinSourceBindingFixture.Unbound }]
            }
        };
        foreach (var request in new[] { Q2CapabilityFixtures.JsonRoundTrip(invalid), Q2CapabilityFixtures.NativeRoundTrip(invalid) })
        {
            var native = store.GetReadDiagnostics();
            global::KeyLoad.QueryPage? result = null;
            var failure = Assert.ThrowsExactly<global::KeyLoad.KeyLoadException>(() => result = engine.ExecuteAst(SqlJoinSourceBindingFixture.Root, request));
            await Assert.That(result).IsNull();
            await Assert.That(failure.Code).IsEqualTo(kind == SqlJoinSourceBindingFixture.Typed ? global::KeyLoad.ErrorCode.Validation : global::KeyLoad.ErrorCode.UnsupportedCapability);
            await Assert.That(failure.Message).IsEqualTo(kind == SqlJoinSourceBindingFixture.Typed ? TypedFailure : kind == SqlJoinSourceBindingFixture.Events ? EventFailure : QueueFailure);
            await Assert.That(store.GetReadDiagnostics()).IsEqualTo(native);
            await UnchangedAsync(store, before, position, identity);
            await SqlJoinSourceBindingAssertions.PageAsync(engine.ExecuteAst(SqlJoinSourceBindingFixture.Root, healthy), kind, position);
            await SqlJoinSourceBindingAssertions.PageAsync(engine.Execute(SqlJoinSourceBindingFixture.Root,
                SqlJoinSourceBindingFixture.Request(partition, kind)), kind, position);
            var replay = owner.ApplyEmbedded(new(command.CommandId, global::KeyLoad.OperationKind.Batch, SqlJoinSourceBindingFixture.Root, default,
                global::System.Text.Json.JsonSerializer.Serialize(command, global::KeyLoad.JsonDefaults.Options)), cancellationToken: default).Get<global::KeyLoad.CommitReceipt>();
            await Assert.That(global::KeyLoad.JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(global::KeyLoad.JsonDefaults.Serialize(receipt))).IsTrue();
            await UnchangedAsync(store, before, position, identity);
        }
    }

    internal static async Task UnchangedAsync(global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, string[] before, long position, byte[] identity)
    {
        await Assert.That(global::KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(before, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(global::KeyLoad.JsonDefaults.Serialize(store.Identity).AsSpan().SequenceEqual(identity)).IsTrue();
    }
}
