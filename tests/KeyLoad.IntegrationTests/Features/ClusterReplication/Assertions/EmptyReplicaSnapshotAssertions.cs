using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.ClusterReplicationTestSupport;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.RetainedReplicaSnapshotScenario;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class EmptyReplicaSnapshotAssertions
{
    private const string Collection = "snapshots";
    private const string ProjectionDocument = "projection";
    private const string ProjectionJson = "{\"done\":true}";
    private const string TailDocument = "ordered-tail";
    private const string TailJson = "{\"tail\":true}";
    private const int DocumentCount = 40;
    private const long FirstRevision = 1;
    private const long NextRevision = 2;

    internal static async Task<EmptyReplicaSnapshotBaseline> CaptureAsync(KeyLoadClient sdk, SnapshotState state, CancellationToken token)
        => new(Success(await sdk.SubscriptionStatusAsync(state.Subscription, token)),
            Success(await sdk.OutboxStatusAsync(state.Partition, token)),
            Success(await sdk.ReadEventSourceAsync(new(state.Subscription.Source), token)),
            Success(await sdk.ReadChangesAsync(new(state.Partition, Collection), token)));

    internal static async Task VerifyAsync(KeyLoadClient sdk, McpOfficialClient mcp, SnapshotState state,
        FinalCommand final, CommandRequest tailCommand, CommitReceipt tail, EmptyReplicaSnapshotBaseline expected, CancellationToken token)
    {
        for (var index = 0; index < DocumentCount; index++)
        {
            var id = "doc-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var json = "{\"n\":" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
            await DocumentAsync(sdk, mcp, new(state.Partition, Collection, id), json, FirstRevision, tail.Token, token);
        }
        await DocumentAsync(sdk, mcp, new(state.Partition, Collection, ProjectionDocument), ProjectionJson, FirstRevision, tail.Token, token);
        await DocumentAsync(sdk, mcp, new(state.Partition, Collection, TailDocument), TailJson, FirstRevision, tail.Token, token);
        await ReceiptAsync(sdk, mcp, final.Command, final.Receipt, token);
        await ReceiptAsync(sdk, mcp, tailCommand, tail, token);
        await HistoryAsync(sdk, mcp, state, expected, token);
        await ProcessingAsync(sdk, mcp, state, token);
    }

    private static async Task HistoryAsync(KeyLoadClient sdk, McpOfficialClient mcp, SnapshotState state,
        EmptyReplicaSnapshotBaseline expected, CancellationToken token)
    {
        var actual = await CaptureAsync(sdk, state, token);
        await ExactAsync(actual.Subscription, expected.Subscription);
        await ExactAsync(actual.Outbox, expected.Outbox);
        await ExactAsync((await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(await mcp.CallAsync(
            McpCallerTools.SubscriptionsStatus, new GetSubscriptionRequest(state.Subscription), token))).Value, expected.Subscription);
        await ExactAsync((await McpCallerAssertions.SuccessAsync<OutboxStatus>(await mcp.CallAsync(
            McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Partition), token))).Value, expected.Outbox);
        await EmptyReplicaSnapshotHistoryAssertions.VerifyAsync(sdk, mcp, state, actual, expected, token);
        await Assert.That(actual.Subscription.Checkpoint).IsEqualTo(FirstRevision);
        await Assert.That(actual.Outbox.Consumers.Single().Checkpoint).IsEqualTo(state.Batch.ThroughSequence);
        await Assert.That(actual.Events.Events).HasSingleItem();
        await Assert.That(actual.Changes.Changes.Length).IsEqualTo(DocumentCount + 2);
    }

    private static async Task ProcessingAsync(KeyLoadClient sdk, McpOfficialClient mcp, SnapshotState state, CancellationToken token)
    {
        await ExactAsync(Success(await sdk.CommitSubscriptionProcessingAsync(state.Processing, token)), state.ProcessingEffect);
        await ExactAsync((await McpCallerAssertions.SuccessAsync<SubscriptionProcessingResult>(await mcp.CallAsync(
            McpCallerTools.SubscriptionsProcess, state.Processing, token))).Value, state.ProcessingEffect);
        await ExactAsync(Success(await sdk.CommitProjectionAsync(state.ProjectionRequest, token)), state.OutboxEffect);
        await ExactAsync((await McpCallerAssertions.SuccessAsync<ProjectionBatchResult>(await mcp.CallAsync(
            McpCallerTools.ProjectionsCommit, state.ProjectionRequest, token))).Value, state.OutboxEffect);
    }

    internal static async Task HealthyAsync(ClusterFixture fixture, string node, SnapshotState state,
        CommandRequest original, CommitReceipt receipt, CancellationToken token)
    {

        var failures = new List<Exception>();
        McpOfficialClient? mcp = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var sdk = fixture.Client(node);
            mcp = await McpOfficialClient.ConnectAsync(fixture, node, fixture.AdminKey, token);
            var next = new CommandRequest(Guid.NewGuid(), state.Partition,
                [new PutDocument(Collection, TailDocument, "{\"tail\":2}", FirstRevision, ExplicitReplacement: true)]);
            var committed = Success(await sdk.CommitAsync(next, token));
            await Assert.That(committed.Token.Incarnation).IsEqualTo(receipt.Token.Incarnation);
            await Assert.That(committed.Token.AtomicPartitionId).IsEqualTo(receipt.Token.AtomicPartitionId);
            await Assert.That(committed.Token.OwnershipEpoch).IsEqualTo(receipt.Token.OwnershipEpoch);
            await Assert.That(committed.Token.Position).IsGreaterThan(receipt.Token.Position);
            await ReceiptAsync(sdk, mcp, next, committed, token);
            await ReceiptAsync(sdk, mcp, original, receipt, token);
            await DocumentAsync(sdk, mcp, new(state.Partition, Collection, TailDocument), "{\"tail\":2}", NextRevision, committed.Token, token);
        }, failures);
        if (mcp is not null)
        { await ServerFailureObserver.ObserveAsync(() => mcp.DisposeAsync().AsTask(), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ReceiptAsync(KeyLoadClient sdk, McpOfficialClient mcp, CommandRequest command,
        CommitReceipt expected, CancellationToken token)
    {
        await ExactAsync(Success(await sdk.CommitAsync(command, token)), expected);
        await ExactAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, token))).Value, expected);
        await Assert.That(expected.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(expected.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
    }

    private static async Task DocumentAsync(KeyLoadClient sdk, McpOfficialClient mcp, EntityRef reference,
        string json, long revision, CommitToken minimum, CancellationToken token)
    {
        var expected = new DocumentResult(reference, revision, json, false, []);
        await ExactAsync(Success(await sdk.GetAsync(reference, minimum, token)), expected);
        await ExactAsync((await McpCallerAssertions.SuccessAsync<DocumentResult>(await mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, minimum), token))).Value, expected);
    }

    internal static async Task ExactAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}

internal sealed record EmptyReplicaSnapshotBaseline(SubscriptionInfo Subscription, OutboxStatus Outbox,
    EventSourcePage Events, ChangeFeedPage Changes);
