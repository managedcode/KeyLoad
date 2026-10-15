using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class RetainedAppendIdentityFixture : IDisposable
{
    internal const string Streams = "identity-streams";
    internal const string Documents = "identity-documents";
    internal const string Queue = "identity-queue";
    internal const string Candidate = "candidate";
    internal const string Target = "target";
    internal const string Foreign = "foreign";
    internal const string Invalid = "A retained event ID does not match its canonical event.";
    internal static readonly DateTimeOffset Epoch = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
    internal TestDatabase Harness { get; } = new(timeProvider: new ControlledReadClock(Epoch));
    internal StreamRef Stream => new(Harness.Partition, Streams, Target);
    internal Dictionary<CommandRequest, OperationResult> Successes { get; } = [];
    internal List<OutboxEntry> ExpectedOutbox { get; } = [];
    internal string[] ColdImage { get; set; } = [];
    internal long ColdPosition
    {
        get; set;
    }
    internal StoreIdentity ColdIdentity { get; set; } = null!;
    internal static EventData Data(string id) => new(id, "Created", "{\"value\":1}", "{\"trace\":\"original\"}");
    internal async Task InitializeAsync()
    {
        Harness.Configure(Streams, ResourceKind.StreamSet);
        Harness.Configure(Documents, ResourceKind.Collection);
        Harness.Configure(Queue, ResourceKind.WorkQueue);
        var command = new CommandRequest(Guid.NewGuid(), Harness.Partition,
            [new AppendEvents(Streams, Target, [Data("initial")], ExpectedStreamRevision.NoStream),
             new AppendEvents(Streams, Foreign, [Data(Candidate)], ExpectedStreamRevision.NoStream)]);
        await CommitAsync(Harness.Database, Harness.Store, command, [new("appendEvents", Streams, Target, 1), new("appendEvents", Streams, Foreign, 1)]);
    }
    internal CommandRequest Producer(string id, string eventId, ExpectedStreamRevision revision) => new(Guid.NewGuid(), Harness.Partition,
        [new PutDocument(Documents, id, "{\"producer\":true}", 0), new EnqueueMessage(Queue, id, "{\"work\":true}"),
         new AppendEvents(Streams, Target, [Data(eventId)], revision)]);
    internal async Task<OperationResult> CommitAsync(DatabaseEngine database, ZoneTreeStore store,
        CommandRequest command, MutationReceipt[] effects)
    {
        var position = store.Position + 1;
        var result = KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Apply(database, OperationKind.Batch,
            command, command.CommandId, Epoch);
        var receipt = result.Get<CommitReceipt>();
        await RetainedAppendIdentityAssertions.ReceiptAsync(receipt, command.CommandId,
            store.Identity.Incarnation, Harness.Partition, position, effects);
        Successes.Add(command, result);
        for (var index = 0; index < command.Mutations.Length; index++)
        {
            var mutation = command.Mutations[index];
            var after = mutation is PutDocument put ? new DocumentRecord(new(Harness.Partition, Documents, put.Id),
                1, "{\"producer\":true}", new(), Epoch) : null;
            ExpectedOutbox.Add(new(ExpectedOutbox.Count + 1L, index, receipt.Token, Epoch, mutation, effects[index], After: after));
        }
        return result;
    }
    public void Dispose() => Harness.Dispose();
}
