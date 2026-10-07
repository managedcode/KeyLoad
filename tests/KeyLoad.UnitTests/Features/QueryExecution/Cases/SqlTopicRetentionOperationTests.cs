using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.UnitTests.Features.EventStreams;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlTopicRetentionOperationTests
{
    private const string RequestKey = "request";
    [Test]
    public async Task AcEventRetention003SharedSqlAndMcpDecoderPurgeTheActualNativeStoreWithOneIdentity()
    {
        using var fixture = new TopicRetentionFixture();
        fixture.ReleasePins();
        var command = fixture.Purge();
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        { [RequestKey] = JsonSerializer.SerializeToElement(command, JsonDefaults.Options) };
        var decoded = SqlOperationTestData.Compile(SqlOperationTestData.Call("keyload_documents_commit", arguments));
        await Assert.That(decoded.CommandKind).IsEqualTo(OperationKind.Batch);
        await Assert.That(decoded.CommandId).IsEqualTo(command.CommandId);
        var payload = NativeSerialization.Deserialize<CommandRequest>(decoded.Payload.Span);
        await Assert.That(payload.Mutations.Single()).IsEqualTo(new PurgeTopic("topic", 2, 1));
        var coordinator = new EmbeddedCoordinator(fixture.Database);
        var receipt = (await coordinator.SubmitNativeAsync(OperationKind.Batch, decoded.CommandId, "root", decoded.Payload))
            .Get<CommitReceipt>();
        await Assert.That(receipt.Mutations.Single()).IsEqualTo(new MutationReceipt("purgeTopic", "topic", "2", 2));
        await TopicRetentionAssertions.RetainedTail(fixture);
        var replay = fixture.Submit(command).Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        await TopicRetentionIdentityFlow.VerifyAsync(fixture);
    }
}
