using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class ProjectionPublicAssertions
{
    internal static async Task ExactAsync<T>(T expected, T actual)
        => await global::TUnit.Assertions.Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static async Task LiteralBatchAsync(ProjectionBatch batch, ProjectionConsumerRef consumer,
        ProjectionConsumerDefinition definition, CommitReceipt sourceReceipt)
    {
        await global::TUnit.Assertions.Assert.That(batch.Entries.Length).IsEqualTo(1);
        var original = batch.Entries.Single();
        var mutation = new PutDocument(ProjectionPublicProtocol.Source, "input", ProjectionPublicProtocol.InputJson, 0L);
        var entry = new OutboxEntry(1L, 0, sourceReceipt.Token, original.CommittedAt, mutation,
            new(ProjectionPublicProtocol.PutKind, ProjectionPublicProtocol.Source, "input", 1L), null,
            new(new(consumer.Partition, ProjectionPublicProtocol.Source, "input"), 1L,
                ProjectionPublicProtocol.InputJson, new RowAccess(), original.CommittedAt));
        await global::TUnit.Assertions.Assert.That(original.CommittedAt.Offset).IsEqualTo(TimeSpan.Zero);
        await ExactAsync(new ProjectionBatch(new(consumer, definition, 0L, false), [entry], 1L, batch.Token, false), batch);
    }

    internal static async Task LiteralReceiptAsync(Guid id, CommitReceipt receipt, params MutationReceipt[] mutations)
    {
        await global::TUnit.Assertions.Assert.That(receipt.CommandId).IsEqualTo(id);
        await ExactAsync(global::System.Collections.Immutable.ImmutableArray.CreateRange(mutations), receipt.Mutations);
        await global::TUnit.Assertions.Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
    }

    internal static async Task<OutboxStatus> PhaseAsync(KeyLoadClient root, ProjectionPublicState state,
        long checkpoint, bool released, bool purged, CancellationToken token)
    {
        var expectedSource = new DocumentReadResult(new(new(state.Partition, ProjectionPublicProtocol.Source, "input"),
            1L, ProjectionPublicProtocol.InputJson, false, []));
        await ExactAsync(expectedSource, await McpCallerAssertions.SdkSuccessAsync(
            await root.GetAsync(expectedSource.Document!.Reference, state.SourceReceipt.Token, token)));
        var expectedEffect = new DocumentReadResult(new(new(state.Partition, ProjectionPublicProtocol.Target, "effect"),
            1L, ProjectionPublicProtocol.EffectJson, false, []));
        await ExactAsync(expectedEffect, await McpCallerAssertions.SdkSuccessAsync(
            await root.GetAsync(expectedEffect.Document!.Reference, state.Completed.Receipt.Token, token)));
        var status = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(state.Partition, token));
        await global::TUnit.Assertions.Assert.That(status.Head.StoredBytes).IsGreaterThanOrEqualTo(0L);
        if (!purged)
        {
            await global::TUnit.Assertions.Assert.That(status.Head.StoredBytes).IsGreaterThan(0L);
        }
        await ExactAsync(new OutboxHead(2L, purged ? 3L : 1L, purged ? 0L : 2L,
            purged ? 0L : status.Head.StoredBytes), status.Head);
        await ExactAsync(global::System.Collections.Immutable.ImmutableArray.Create(new ProjectionConsumerInfo(
            state.Consumer, state.Definition, checkpoint, released)), status.Consumers);
        return status;
    }

    internal static async Task ErrorAsync(RequestCqrsClusterCallers caller, CommitProjectionBatchRequest request,
        ErrorCode code, CancellationToken token)
    {
        var result = await caller.Sdk.CommitProjectionAsync(request, token);
        await global::TUnit.Assertions.Assert.That(result.IsSuccess).IsFalse();
        await global::TUnit.Assertions.Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
        await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.ProjectionsCommit, request, token), code, dispatched: true);
    }
}
