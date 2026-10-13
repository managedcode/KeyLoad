namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal static class QueueGraphReplayFlow
{
    private const string CanonicalVertexMismatch = "A canonical graph vertex record is inconsistent.";

    internal static async Task RunAsync(TestDatabase db, bool from, int component)
    {
        db.Configure(QueueGraphIdentityState.Collection, ResourceKind.Collection);
        db.Configure(QueueGraphIdentityState.Graph, ResourceKind.Graph);
        db.Configure(QueueGraphIdentityState.Queue, ResourceKind.WorkQueue);
        var source = await QueueGraphIdentityFlow.SeedAsync(db, from);
        await RunSeededAsync(source, from, component);
    }

    internal static async Task RunSeededAsync(QueueGraphIdentityState source, bool from, int component)
    {
        source = source with { FailureDetail = CanonicalVertexMismatch };
        var db = source.Fixture;
        await QueueGraphIdentityAssertions.HealthyAsync(source, db.Database, db.Store);
        var reverseCommand = new CommandRequest(Guid.NewGuid(), db.Partition,
            [new GraphToQueueMutation(QueueGraphIdentityState.Queue, QueueGraphIdentityState.Graph,
                source.From, QueueGraphReplayState.ReversePrefix)]);
        var reverse = await QueueGraphIdentityFlow.CompileAsync(reverseCommand, source.Healthy.EvaluatedAt.AddSeconds(1));
        var receipt = db.Database.Apply(reverse).Get<CommitReceipt>();
        await QueueGraphReplayAssertions.ReverseReceiptAsync(source, db.Store, reverse.Id, receipt);
        var next = await QueueGraphIdentityFlow.CompileAsync(new(Guid.NewGuid(), db.Partition,
            [new PutDocument(QueueGraphIdentityState.Collection, QueueGraphReplayState.Continuation,
                QueueGraphReplayState.ContinuationJson),
                new GraphToQueueMutation(QueueGraphIdentityState.Queue, QueueGraphIdentityState.Graph,
                    source.From, QueueGraphReplayState.NextPrefix)]), reverse.EvaluatedAt.AddSeconds(1));
        var state = new QueueGraphReplayState(source, reverse, NativeSerialization.Serialize(receipt), next);
        await QueueGraphReplayAssertions.ReplaysAsync(state, db.Database, db.Store, continued: false);
        var reference = from ? source.From : source.To;
        var foreign = new PartitionRef(db.Partition.TenantId, db.Partition.DatabaseId,
            db.Partition.TransactionDomainId, QueueGraphIdentityState.ForeignPartition);
        var mismatched = component switch
        {
            0 => reference with { Partition = foreign },
            1 => reference with { Collection = QueueGraphReplayState.OtherEndpoint },
            2 => reference with { Id = QueueGraphReplayState.OtherEndpoint },
            _ => throw new ArgumentOutOfRangeException(nameof(component))
        };
        var record = from ? source.First : source.Second;
        db.Store.Commit((tx, _) =>
        { tx.Put(source.EndpointKey, NativeSerialization.Serialize(record with { Reference = mismatched })); return true; });
        await QueueGraphReplayAssertions.RefusesAsync(state, db.Database, db.Store);
        source.ColdImage = global::KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(db.Store);
        source.ColdPosition = db.Store.Position;
        db.Store.Dispose();
        await QueueGraphReplayCold.RunAsync(state, dirty: true);
        await QueueGraphReplayCold.RunAsync(state, dirty: true, repair: true);
        await QueueGraphReplayCold.RunAsync(state, dirty: false);
        await QueueGraphReplayCold.RunAsync(state, dirty: false);
    }
}
