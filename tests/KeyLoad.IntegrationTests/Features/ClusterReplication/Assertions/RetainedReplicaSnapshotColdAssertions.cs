using System.Globalization;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class RetainedReplicaSnapshotColdAssertions
{
    private const string Collection = "snapshots";
    private const string ProjectionDocument = "projection";
    private const string ProjectionJson = "{\"done\":true}";
    private const string HealthyDocument = "retained-cold-healthy";
    private const string HealthyJson = "{\"cold\":true}";
    private const int DocumentCount = 40;
    private const int HistoryCount = 41;
    private const long Revision = 1;

    internal static async Task FullAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RetainedReplicaSnapshotScenario.SnapshotState state,
        RetainedReplicaSnapshotScenario.FinalCommand final, CancellationToken token)
    {
        await ReceiptAsync(sdk, mcp, final.Command, final.Receipt, token).ConfigureAwait(false);
        await EmptyReplicaSnapshotAssertions.ExactAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.CommitSubscriptionProcessingAsync(state.Processing, token).ConfigureAwait(false)),
            state.ProcessingEffect);
        await EmptyReplicaSnapshotAssertions.ExactAsync((await McpCallerAssertions.SuccessAsync<SubscriptionProcessingResult>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsProcess, state.Processing, token).ConfigureAwait(false))).Value,
            state.ProcessingEffect);
        await EmptyReplicaSnapshotAssertions.ExactAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.CommitProjectionAsync(state.ProjectionRequest, token).ConfigureAwait(false)), state.OutboxEffect);
        await EmptyReplicaSnapshotAssertions.ExactAsync((await McpCallerAssertions.SuccessAsync<ProjectionBatchResult>(
            await mcp.CallAsync(McpCallerTools.ProjectionsCommit, state.ProjectionRequest, token).ConfigureAwait(false))).Value,
            state.OutboxEffect);
        for (var index = 0; index < DocumentCount; index++)
        {
            token.ThrowIfCancellationRequested();
            var number = index.ToString(CultureInfo.InvariantCulture);
            await DocumentAsync(sdk, mcp, new(state.Partition, Collection, "doc-" + number),
                "{\"n\":" + number + "}", final.Receipt.Token, token).ConfigureAwait(false);
        }
        await DocumentAsync(sdk, mcp, new(state.Partition, Collection, ProjectionDocument),
            ProjectionJson, final.Receipt.Token, token).ConfigureAwait(false);
        var changes = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadChangesAsync(
            new(state.Partition, Collection), token).ConfigureAwait(false));
        await Assert.That(changes.Changes.Length).IsEqualTo(HistoryCount);
        var checkpoint = await McpCallerAssertions.SdkSuccessAsync(
            await sdk.SubscriptionStatusAsync(state.Subscription, token).ConfigureAwait(false));
        await Assert.That(checkpoint.Checkpoint).IsEqualTo(Revision);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(
            await sdk.OutboxStatusAsync(state.Partition, token).ConfigureAwait(false));
        await Assert.That(outbox.Consumers.Single().Checkpoint).IsEqualTo(state.Batch.ThroughSequence);
    }

    internal static async Task HealthyAsync(ClusterFixture fixture, string node,
        RetainedReplicaSnapshotScenario.SnapshotState state,
        RetainedReplicaSnapshotScenario.FinalCommand final, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var mcp = await McpOfficialClient.ConnectAsync(
                fixture, node, fixture.AdminKey, token).ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var sdk = fixture.Client(node);
                var next = new CommandRequest(Guid.NewGuid(), state.Partition,
                    [new PutDocument(Collection, HealthyDocument, HealthyJson, 0)]);
                var receipt = await McpCallerAssertions.SdkSuccessAsync(
                    await sdk.CommitAsync(next, token).ConfigureAwait(false));
                await Assert.That(receipt.Token.Incarnation).IsEqualTo(final.Receipt.Token.Incarnation);
                await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(final.Receipt.Token.AtomicPartitionId);
                await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(final.Receipt.Token.OwnershipEpoch);
                await Assert.That(receipt.Token.Position).IsGreaterThan(final.Receipt.Token.Position);
                await EmptyReplicaSnapshotAssertions.ExactAsync(receipt.Mutations,
                    global::System.Collections.Immutable.ImmutableArray.Create(
                        new MutationReceipt("putDocument", Collection, HealthyDocument, Revision)));
                await ReceiptAsync(sdk, mcp, next, receipt, token).ConfigureAwait(false);
                await ReceiptAsync(sdk, mcp, final.Command, final.Receipt, token).ConfigureAwait(false);
                await DocumentAsync(sdk, mcp, new(state.Partition, Collection, HealthyDocument),
                    HealthyJson, receipt.Token, token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ReceiptAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        CommandRequest request, CommitReceipt expected, CancellationToken token)
    {
        await EmptyReplicaSnapshotAssertions.ExactAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.CommitAsync(request, token).ConfigureAwait(false)), expected);
        await EmptyReplicaSnapshotAssertions.ExactAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.DocumentsCommit, request, token).ConfigureAwait(false))).Value, expected);
        await Assert.That(expected.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(expected.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
    }

    private static async Task DocumentAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        EntityRef reference, string json, CommitToken minimum, CancellationToken token)
    {
        var expected = new DocumentResult(reference, Revision, json, false, []);
        await EmptyReplicaSnapshotAssertions.ExactAsync(await PublicReadAssertions.DocumentAsync(
            await sdk.GetAsync(reference, minimum, token).ConfigureAwait(false)), expected);
        await EmptyReplicaSnapshotAssertions.ExactAsync((await PublicReadAssertions.DocumentAsync(
            await mcp.CallAsync(McpCallerTools.DocumentsGet,
                new GetDocumentRequest(reference, minimum), token).ConfigureAwait(false))).Value, expected);
    }
}
