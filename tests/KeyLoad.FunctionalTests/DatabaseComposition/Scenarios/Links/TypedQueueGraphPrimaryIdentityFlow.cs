namespace KeyLoad.FunctionalTests.DatabaseComposition;

internal static class TypedQueueGraphPrimaryIdentityFlow
{
    private const string ForeignPrimaryJson = """{"id":"foreign","name":"literal"}""";
    private const string MissingPrimaryJson = """{"name":"literal"}""";
    private const string NonTextPrimaryJson = """{"id":7,"name":"literal"}""";
    private const string MalformedPrimaryJson = "{";
    private const string NonObjectPrimaryJson = "[]";

    private const string DuplicateFirstPrimaryJson = """{"id":"foreign","id":"first","name":"first"}""";
    private const string DuplicateSecondPrimaryJson = """{"id":"foreign","id":"second","name":"second"}""";

    internal static IEnumerable<string> Faults =>
        [ForeignPrimaryJson, MissingPrimaryJson, NonTextPrimaryJson, MalformedPrimaryJson, NonObjectPrimaryJson,
         DuplicateFirstPrimaryJson, DuplicateSecondPrimaryJson];

    internal static async Task RunAsync(TestDatabase db, bool from, string json)
    {
        var source = await TypedQueueGraphReplayFixture.SeedAsync(db, from);
        source = source with
        {
            Healthy = source.Failed
        };
        var record = from ? source.First : source.Second;
        await QueueGraphIdentityFlow.RunFaultAsync(source,
            OrleansSerialization.Serialize(record with
            {
                Json = json
            }));
        await ContinueAsync(source);
    }

    private static async Task ContinueAsync(QueueGraphIdentityState source)
    {
        var failures = new List<Exception>();
        QueueGraphReplayState? replay = null;
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            var identity = source.Fixture.Store.Identity;
            using var store = global::KeyLoad.Storage.ZoneTree.ZoneTreeExistingStore.Open(
                new(source.Fixture.Directory)
                {
                    Incarnation = identity.Incarnation
                },
                identity.NodeId, source.Fixture.StorageExecution);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(JsonDefaults.Serialize(store.Identity).AsSpan()
                    .SequenceEqual(JsonDefaults.Serialize(identity))).IsTrue();
                await Assert.That(store.Position).IsEqualTo(source.ColdPosition);
                await Assert.That(global::KeyLoad.FunctionalTests.Messaging.QueueWholeFlowStorage.Bytes(store))
                    .IsEquivalentTo(source.ColdImage, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
                var engine = source.Fixture.CreateConfiguredDatabase(store);
                var reverse = await QueueGraphIdentityFlow.CompileAsync(engine, new(Guid.NewGuid(), source.Fixture.Partition,
                    [new GraphToQueueMutation(QueueGraphIdentityState.Queue, QueueGraphIdentityState.Graph,
                        source.From, QueueGraphReplayState.ReversePrefix)]),
                    new DateTimeOffset(source.Healthy.Timestamp).AddSeconds(1));
                var nextPosition = checked(engine.LastApplied + 1);
                var receipt = engine.Apply(reverse).Get<CommitReceipt>();
                await QueueGraphReplayAssertions.ReverseReceiptAsync(source, store, reverse.CommandId, receipt, nextPosition);
                var continuation = store.Read(view => DatabaseCompositionExpectedReferences.Capture(view,
                    new(source.Fixture.Partition, QueueGraphIdentityState.Collection, QueueGraphReplayState.Continuation)));
                var next = await QueueGraphIdentityFlow.CompileAsync(engine, new(Guid.NewGuid(), source.Fixture.Partition,
                    [new PutDocument(QueueGraphIdentityState.Collection, QueueGraphReplayState.Continuation,
                        QueueGraphReplayState.ContinuationJson),
                     new GraphToQueueMutation(QueueGraphIdentityState.Queue, QueueGraphIdentityState.Graph,
                        source.From, QueueGraphReplayState.NextPrefix)]),
                    new DateTimeOffset(reverse.Timestamp).AddSeconds(1));
                replay = new(source, reverse, OrleansSerialization.Serialize(receipt), next, continuation);
                await QueueGraphReplayAssertions.ContinueAsync(replay, engine, store);
                source.ColdImage = global::KeyLoad.FunctionalTests.Messaging.QueueWholeFlowStorage.Bytes(store);
                source.ColdPosition = store.Position;
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        await QueueGraphReplayCold.RunAsync(replay!, dirty: false);
        await QueueGraphReplayCold.RunAsync(replay!, dirty: false);
    }
}
