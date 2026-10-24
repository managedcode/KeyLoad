using System.Text.Json;
using KeyLoad.UnitTests.ResourceExecution;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Exceptions;

namespace KeyLoad.UnitTests.EventStreams;

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
        var operation = Operation(fixture.Database, Guid.NewGuid(), command);

        var result = fixture.Apply(operation);

        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.Validation);
        await Assert.That(result.Problem?.Detail).IsEqualTo("The envelope and command IDs differ.");
        await Assert.That(fixture.EventSourceHead.TailPosition).IsEqualTo(0L);
        await Assert.That(fixture.Read().Events).IsEmpty();
    }

    [Test]
    public async Task AcEvent004MatchedEnvelopeKeepsReceiptIdAndStableRetryWithoutExtraRevision()
    {
        using var fixture = new StreamReadResourceFixture();
        var commandId = Guid.NewGuid();
        var data = RetainedAppendIdentityFixture.Data(EventId);
        var command = new CommandRequest(commandId, fixture.Partition,
            [new AppendEvents(StreamSetName, StreamId, [data], ExpectedStreamRevision.NoStream)]);
        var operation = Operation(fixture.Database, commandId, command);

        var committed = fixture.Apply(operation);
        var receipt = committed.Get<CommitReceipt>();
        var replay = fixture.Apply(operation);

        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await ReplayResultAssertions.Same<CommitReceipt>(replay, committed);
        await Assert.That(fixture.EventSourceHead.TailPosition).IsEqualTo(1L);
        await Assert.That(fixture.Read().Events.Select(record => record.Revision).SequenceEqual([1L])).IsTrue();
        await Assert.That(fixture.Read().Events.Single().Data).IsEqualTo(data);

        var duplicate = new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new AppendEvents(StreamSetName, StreamId, [data], ExpectedStreamRevision.Exact(1))]);
        var duplicateResult = fixture.Apply(Operation(fixture.Database, duplicate.CommandId, duplicate));
        await Assert.That(Errors.Code(duplicateResult.Problem)).IsEqualTo(ErrorCode.DuplicateEventId);
        await Assert.That(duplicateResult.Value).IsNull();
        var changed = data with
        {
            PayloadJson = "{\"a\":\"A\",\"minusZero\":0,\"z\":1}",
            HeadersJson = "{\"a\":\"o\",\"z\":\"trace\"}"
        };
        var conflict = new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [new AppendEvents(StreamSetName, StreamId, [changed], ExpectedStreamRevision.Exact(1))]);
        var conflicted = fixture.Apply(Operation(fixture.Database, conflict.CommandId, conflict));
        await Assert.That(Errors.Code(conflicted.Problem)).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(conflicted.Problem?.Detail).IsEqualTo("The event ID was reused with different content.");
        await Assert.That(conflicted.Value).IsNull();
        await Assert.That(fixture.EventSourceHead.TailPosition).IsEqualTo(1L);
        await Assert.That(fixture.Read().Events.Single().Data).IsEqualTo(data);
        await ReplayResultAssertions.Same<CommitReceipt>(fixture.Apply(operation), committed);
    }

    [Test]
    public async Task AcR14ReplayComparisonRejectsChangedInternalCompositionReferences()
    {
        using var fixture = new StreamReadResourceFixture();
        var commandId = Guid.NewGuid();
        var committed = fixture.Apply(Operation(fixture.Database, commandId, Append(fixture.Partition, commandId)));
        var receipt = committed.Get<CommitReceipt>();
        var original = receipt.Mutations.Single();
        var mutation = original with
        {
            CompositionReferences = original.CompositionReferences.Add(new(fixture.Partition, StreamSetName, StreamId))
        };
        var changed = receipt with
        {
            Mutations = [mutation]
        };
        await Assert.ThrowsExactlyAsync<AssertionException>(() => ReplayResultAssertions.Same<CommitReceipt>(
            Result<object>.Succeed(changed), committed));
    }

    private static Command<DatabaseCommand> Operation(DatabaseEngine database, Guid envelopeId, CommandRequest command)
        => database.CreateCommand(OperationKind.Batch, envelopeId, AdministratorId, database.EvaluationClock.GetUtcNow(),
            JsonSerializer.Serialize(command, JsonDefaults.Options));

    private static CommandRequest Append(PartitionRef partition, Guid commandId)
        => new(commandId, partition,
            [new AppendEvents(StreamSetName, StreamId, [new(EventId, EventType, EventBody)], ExpectedStreamRevision.NoStream)]);
}
