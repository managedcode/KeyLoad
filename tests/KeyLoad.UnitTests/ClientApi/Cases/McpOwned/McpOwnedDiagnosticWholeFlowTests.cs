using System.Text.Json;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class McpOwnedDiagnosticWholeFlowTests
{
    private const string Root = "root";
    private const string Collection = "mcp-session-documents";
    private const string Document = "one";
    private const string Json = "{\"value\":\"literal\"}";
    private const long First = 1;
    private const long Absent = 0;
    private const int CommitAdvance = 1;

    [Test]
    public async Task ForeignSessionReadKeepsExactOwnedProblemNoEffectThenLiteralHealthyReply()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, database.Partition,
            [new PutDocument(Collection, Document, Json, ExpectedRevision: Absent)]);
        var receipt = database.Database.Apply(new(id, OperationKind.Batch, Root,
            database.Database.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(command, JsonDefaults.Options)), First).Get<CommitReceipt>();
        var reference = new EntityRef(database.Partition, Collection, Document);
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        (CommitToken Token, string Detail)[] rejected =
        [
            (receipt.Token with { Incarnation = Guid.NewGuid() }, McpOwnedDiagnosticAssertions.ForeignIncarnation),
            (receipt.Token with { AtomicPartitionId = Guid.NewGuid().ToString() }, McpOwnedDiagnosticAssertions.OutOfScope),
            (receipt.Token with { OwnershipEpoch = receipt.Token.OwnershipEpoch + First }, McpOwnedDiagnosticAssertions.OutOfScope),
            (receipt.Token with { Position = long.MaxValue }, McpOwnedDiagnosticAssertions.FuturePosition),
            (receipt.Token with { Position = Absent }, McpOwnedDiagnosticAssertions.InvalidPosition)
        ];
        foreach (var invalid in rejected)
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.GetDocument(Root, reference, invalid.Token));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.TokenInvalidated);
            await Assert.That(error.Message).IsEqualTo(invalid.Detail);
            await McpOwnedDiagnosticAssertions.ReplyAsync(error.Code, error.Message, invalid.Detail);
            await McpOwnedDiagnosticAssertions.ReplyAsync(error.Code,
                error.Message + McpOwnedDiagnosticAssertions.PrivateCanary, McpOwnedDiagnosticAssertions.Generic);
            await McpOwnedDiagnosticAssertions.ReplyAsync(ErrorCode.Validation, error.Message, McpOwnedDiagnosticAssertions.Generic);
            await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(before, CollectionOrdering.Matching);
            await Assert.That(database.Store.Position).IsEqualTo(position);
            var next = database.Database.GetDocument(Root, reference, receipt.Token);
            await Assert.That(NativeSerialization.Serialize(next).SequenceEqual(NativeSerialization.Serialize(
                new DocumentResult(reference, First, Json, false, [])))).IsTrue();
        }
        var healthy = database.Database.GetDocument(Root, reference, receipt.Token);
        var expected = new DocumentResult(reference, First, Json, false, []);
        await Assert.That(NativeSerialization.Serialize(healthy).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
        using var response = KeyLoad.Server.McpReplyOwner.Success(JsonDefaults.Serialize(healthy), Guid.NewGuid(),
            McpOutputTestData.MaximumBytes, UnitMcpOptions.Execution());
        await Assert.That(JsonElement.DeepEquals(response.ToolResult().StructuredContent!.Value
            .GetProperty(McpOutputTestData.ResultField), JsonSerializer.SerializeToElement(expected, JsonDefaults.Options))).IsTrue();
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConfiguredVectorFailureKeepsExactOwnedProblemStableReceiptThenLiteralHealthyReply(bool projection)
    {
        using var database = new TestDatabase();
        ConfiguredVectorProfileFlow.Seed(database);
        var rejectedCommand = ConfiguredVectorProfileFlow.Command(database, projection, wrongModel: true);
        var model = ConfiguredVectorProfileFlow.ModelBytes(database, rejectedCommand);
        var position = database.Store.Position;
        var rejected = database.Submit(OperationKind.Batch, rejectedCommand, id: rejectedCommand.CommandId);
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(rejected.SafeDetail).IsEqualTo(McpOwnedDiagnosticAssertions.VectorMismatch);
        await Assert.That(rejected.Json).IsNull();
        await McpOwnedDiagnosticAssertions.ReplyAsync(ErrorCode.Validation, rejected.SafeDetail!, McpOwnedDiagnosticAssertions.VectorMismatch);
        await Assert.That(ConfiguredVectorProfileFlow.ModelBytes(database, rejectedCommand)).IsEquivalentTo(model, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position + CommitAdvance);
        var retained = QueueWholeFlowStorage.Bytes(database.Store);
        var replay = database.Submit(OperationKind.Batch, rejectedCommand, id: rejectedCommand.CommandId);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(rejected))).IsTrue();
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(retained, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position + CommitAdvance);
        var healthyCommand = ConfiguredVectorProfileFlow.Command(database, projection, wrongModel: false);
        var healthy = database.Submit(OperationKind.Batch, healthyCommand, id: healthyCommand.CommandId).Get<CommitReceipt>();
        await McpOwnedDiagnosticAssertions.VectorReceiptAsync(healthy, healthyCommand, projection);
        await ConfiguredVectorProfileFlow.HealthyAsync(database.Database, database.Partition, TestContext.Current!.Execution.CancellationToken);
        using var response = KeyLoad.Server.McpReplyOwner.Success(JsonDefaults.Serialize(healthy), Guid.NewGuid(),
            McpOutputTestData.MaximumBytes, UnitMcpOptions.Execution());
        await Assert.That(JsonElement.DeepEquals(response.ToolResult().StructuredContent!.Value
            .GetProperty(McpOutputTestData.ResultField), JsonSerializer.SerializeToElement(healthy, JsonDefaults.Options))).IsTrue();
    }
}
