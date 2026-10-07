using System.Text.Json;
using KeyLoad.Core;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class EventAppendWholeFlowFixture : IDisposable
{
    internal const string Root = "root";
    internal const string Documents = "orders";
    internal const string Streams = "events";
    internal const string Queue = "jobs";
    internal const string StreamId = "stream";
    internal const string InitialId = "initial";
    private static readonly string[] Families = ["document", "document-epoch", "index", "unique",
        "stream-head", "event", "event-id", "event-feed", "event-sequence", "message-body",
        "message-meta", "queue-counters", "ready", "scheduled", "lease", "dead-letter", "inbox"];
    internal TestDatabase Database { get; } = new();
    internal DateTimeOffset Now { get; }
    internal StreamRef Stream => new(Database.Partition, Streams, StreamId);
    internal QueueLaneRef Lane => new(Database.Partition, Queue);

    internal EventAppendWholeFlowFixture()
    {
        try
        {
            Database.Configure(Documents, ResourceKind.Collection);
            Database.Configure(Streams, ResourceKind.StreamSet);
            Database.Configure(Queue, ResourceKind.WorkQueue);
            Now = Database.Database.EvaluationClock.GetUtcNow();
            var id = Guid.NewGuid();
            Submit(new(id, Database.Partition, [new AppendEvents(Streams, StreamId,
                [Data(InitialId)], ExpectedStreamRevision.NoStream)])).Get<CommitReceipt>();
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            KeyLoad.Server.ServerFailureObserver.Observe(Database.Dispose, failures);
            KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal OperationResult Submit(CommandRequest command)
        => Database.Submit(OperationKind.Batch, command, id: command.CommandId, time: Now);

    internal CommandRequest Producer(string identity, ExpectedStreamRevision expected,
        params EventData[] events)
        => new(Guid.NewGuid(), Database.Partition,
            [new PutDocument(Documents, identity, "{\"producer\":true}", 0),
                new EnqueueMessage(Queue, identity, "{\"work\":true}"),
                new AppendEvents(Streams, StreamId, [.. events], expected)]);

    internal static EventData Data(string id, string body = "{}") => new(id, "Created", body);

    internal string[] DomainBytes() => Database.Store.Read(view => Families.SelectMany(family =>
    {
        var page = view.Scan(KeySpace.Partition(family, Database.Partition), 4096);
        if (page.HasMore)
        { throw new InvalidOperationException("Whole domain state exceeded the fixture bound."); }
        return page.Records.Select(record => Convert.ToHexString(record.Key.Span) + ":" +
            Convert.ToHexString(record.Value.Span));
    }).ToArray());

    internal async Task AssertPageAsync(params EventData[] expected)
    {
        var page = Database.Database.ReadStream(Root, Stream);
        await Assert.That(page.Stream).IsEqualTo(Stream);
        await Assert.That(page.Head).IsEqualTo(new StreamHead(expected.Length, 1, 1));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.CutPosition).IsEqualTo(Database.Store.Position);
        var records = expected.Select((data, index) => new EventRecord(Stream, index + 1L,
            index + 1L, data, Now));
        await Assert.That(page.Events.Select(value => JsonSerializer.Serialize(value, JsonDefaults.Options)))
            .IsEquivalentTo(records.Select(value => JsonSerializer.Serialize(value, JsonDefaults.Options)), CollectionOrdering.Matching);
    }

    internal async Task AssertProducerAsync(string identity, bool present)
    {
        var document = Database.Database.GetDocument(Root, new(Database.Partition, Documents, identity));
        var message = Database.Database.InspectMessage(Root, Lane, identity);
        if (!present)
        {
            await Assert.That(document).IsNull();
            await Assert.That(message).IsNull();
            return;
        }
        await Assert.That(document!.Reference).IsEqualTo(new EntityRef(Database.Partition, Documents, identity));
        await Assert.That(document.Redacted).IsFalse();
        await Assert.That(document.RedactedFields).IsEmpty();
        await Assert.That(document.Revision).IsEqualTo(1L);
        await Assert.That(document.Json).IsEqualTo("{\"producer\":true}");
        await Assert.That(message!.PayloadJson).IsEqualTo("{\"work\":true}");
        await Assert.That(message.HeadersJson).IsEqualTo("{}");
        await Assert.That(message.Metadata.Id).IsEqualTo(identity);
        await Assert.That(message.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(message.Metadata.Attempts).IsEqualTo(0);
    }

    internal async Task AssertFailedReplayAsync(CommandRequest command, OperationResult original)
    {
        var position = Database.Store.Position;
        var before = KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(Database.Store);
        var retry = Submit(command);
        await Assert.That(NativeSerialization.Serialize(retry))
            .IsEquivalentTo(NativeSerialization.Serialize(original), CollectionOrdering.Matching);
        await Assert.That(retry.Json).IsEqualTo(original.Json);
        await Assert.That(retry.Error).IsEqualTo(original.Error);
        await Assert.That(retry.SafeDetail).IsEqualTo(original.SafeDetail);
        await Assert.That(retry.NativeValue).IsNull();
        await Assert.That(original.NativeValue).IsNull();
        await Assert.That(Database.Store.Position).IsEqualTo(position);
        await Assert.That(KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(Database.Store))
            .IsEquivalentTo(before, CollectionOrdering.Matching);
    }

    public void Dispose() => Database.Dispose();
}
