using System.Text.Json;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class StreamCommandIdentityTests
{
    private const string AdministratorId = "root";
    private const string StreamSetName = "events";
    private const string StreamId = "stream-1";
    private const string EventId = "event-1";
    private const string EventType = "Created";
    private const string EventBody = "{}";

    [Test]
    public async Task AcEvent004MismatchedEnvelopeAndBatchIdsRejectWithoutStreamEffects()
    {
        using var fixture = new StreamReadResourceFixture();
        var commandId = Guid.NewGuid();
        var command = Append(fixture.Partition, commandId);
        var operation = Operation(Guid.NewGuid(), command);

        var result = fixture.Apply(operation);

        await Assert.That(result.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(result.SafeDetail).IsEqualTo("The envelope and command IDs differ.");
        await Assert.That(fixture.EventSourceHead.TailPosition).IsEqualTo(0L);
        await Assert.That(fixture.Read().Events).IsEmpty();
    }

    [Test]
    public async Task AcEvent004MatchedEnvelopeKeepsReceiptIdAndStableRetryWithoutExtraRevision()
    {
        using var fixture = new StreamReadResourceFixture();
        var commandId = Guid.NewGuid();
        var operation = Operation(commandId, Append(fixture.Partition, commandId));

        var committed = fixture.Apply(operation);
        var receipt = committed.Get<CommitReceipt>();
        var replay = fixture.Apply(operation);

        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(replay).IsEqualTo(committed);
        await Assert.That(fixture.EventSourceHead.TailPosition).IsEqualTo(1L);
        await Assert.That(fixture.Read().Events.Select(record => record.Revision).SequenceEqual([1L])).IsTrue();
    }

    private static ReplicatedOperation Operation(Guid envelopeId, CommandRequest command)
        => new(envelopeId, OperationKind.Batch, AdministratorId, TimeProvider.System.GetUtcNow(),
            JsonSerializer.Serialize(command, JsonDefaults.Options));

    private static CommandRequest Append(PartitionRef partition, Guid commandId)
        => new(commandId, partition,
            [new AppendEvents(StreamSetName, StreamId, [new(EventId, EventType, EventBody)], ExpectedStreamRevision.NoStream)]);
}
