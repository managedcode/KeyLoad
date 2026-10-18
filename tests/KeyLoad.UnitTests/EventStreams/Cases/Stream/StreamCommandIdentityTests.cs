using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Exceptions;

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
        var operation = Operation(fixture.Database, commandId, Append(fixture.Partition, commandId));

        var committed = fixture.Apply(operation);
        var receipt = committed.Get<CommitReceipt>();
        var replay = fixture.Apply(operation);

        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await NativeReplayResultAssertions.Same<CommitReceipt>(replay, committed);
        await Assert.That(fixture.EventSourceHead.TailPosition).IsEqualTo(1L);
        await Assert.That(fixture.Read().Events.Select(record => record.Revision).SequenceEqual([1L])).IsTrue();
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
        await Assert.ThrowsExactlyAsync<AssertionException>(() => NativeReplayResultAssertions.Same<CommitReceipt>(
            Result<object>.Succeed(changed), committed));
    }

    private static Command<DatabaseCommand> Operation(DatabaseEngine database, Guid envelopeId, CommandRequest command)
        => database.CreateCommand(OperationKind.Batch, envelopeId, AdministratorId, database.EvaluationClock.GetUtcNow(),
            JsonSerializer.Serialize(command, JsonDefaults.Options));

    private static CommandRequest Append(PartitionRef partition, Guid commandId)
        => new(commandId, partition,
            [new AppendEvents(StreamSetName, StreamId, [new(EventId, EventType, EventBody)], ExpectedStreamRevision.NoStream)]);
}
