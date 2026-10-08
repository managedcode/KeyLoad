using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class McpCommandConflictDiagnosticWholeFlowTests
{
    private const string Root = "root";
    private const string Collection = "mcp-conflict-documents";
    private const string Document = "one";
    private const string OriginalJson = "{\"value\":\"literal\"}";
    private const string ChangedJson = "{\"value\":\"private-diagnostic-canary\"}";
    private const string HealthyJson = "{\"value\":\"healthy\"}";
    private const string Conflict = "The command ID was already used with different content.";
    private const string MutationKind = "putDocument";
    private const long Absent = 0;
    private const long First = 1;
    private const long Second = 2;

    [Test]
    public async Task NativeReplayConflictKeepsOwnedProblemAndStateThenHealthyCommand()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        var command = new CommandRequest(Guid.NewGuid(), database.Partition,
            [new PutDocument(Collection, Document, OriginalJson, ExpectedRevision: Absent)]);
        var original = database.Submit(OperationKind.Batch, command, id: command.CommandId);
        var receipt = original.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var replay = database.Submit(OperationKind.Batch, command, id: command.CommandId);
        await Assert.That(NativeSerialization.Serialize(replay).AsSpan().SequenceEqual(NativeSerialization.Serialize(original))).IsTrue();
        var changed = command with { Mutations = [new PutDocument(Collection, Document, ChangedJson, ExpectedRevision: First)] };
        var rejected = database.Submit(OperationKind.Batch, changed, id: command.CommandId);
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(rejected.SafeDetail).IsEqualTo(Conflict);
        await Assert.That(rejected.Json).IsNull();
        await McpOwnedDiagnosticAssertions.ReplyAsync(ErrorCode.Conflict, rejected.SafeDetail!, Conflict);
        await McpOwnedDiagnosticAssertions.ReplyAsync(ErrorCode.Conflict,
            Conflict + McpOwnedDiagnosticAssertions.PrivateCanary, McpOwnedDiagnosticAssertions.Generic);
        await McpOwnedDiagnosticAssertions.ReplyAsync(ErrorCode.Validation, Conflict, McpOwnedDiagnosticAssertions.Generic);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        var healthyCommand = new CommandRequest(Guid.NewGuid(), database.Partition,
            [new PutDocument(Collection, Document, HealthyJson, ExpectedRevision: First)]);
        var healthy = database.Submit(OperationKind.Batch, healthyCommand, id: healthyCommand.CommandId).Get<CommitReceipt>();
        await Assert.That(healthy.CommandId).IsEqualTo(healthyCommand.CommandId);
        await Assert.That(healthy.Token.AtomicPartitionId).IsEqualTo(database.Partition.AtomicPartitionId);
        await Assert.That(healthy.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(healthy.Mutations).HasSingleItem();
        await Assert.That(NativeSerialization.Serialize(healthy.Mutations[0]).AsSpan().SequenceEqual(
            NativeSerialization.Serialize(new MutationReceipt(MutationKind, Collection, Document, Second)))).IsTrue();
        var reference = new EntityRef(database.Partition, Collection, Document);
        var actual = database.Database.GetDocument(Root, reference);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(
            new DocumentResult(reference, Second, HealthyJson, false, [])))).IsTrue();
    }
}
