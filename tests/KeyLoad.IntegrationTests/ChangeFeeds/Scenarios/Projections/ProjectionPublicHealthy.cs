using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.ChangeFeeds;

internal static class ProjectionPublicHealthy
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers caller,
        ProjectionPublicSettlement settled, CancellationToken token)
    {
        var state = settled.Original;
        var consumer = new ProjectionConsumerRef(state.Partition, ProjectionPublicProtocol.NextConsumer);
        var definition = state.Definition with
        {
            IndexGeneration = 2L
        };
        var configure = new ConfigureProjectionConsumerRequest(Guid.NewGuid(), consumer, definition, 2L);
        var configured = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ConfigureProjectionAsync(configure, token));
        await ProjectionPublicAssertions.ExactAsync(new ProjectionConsumerInfo(consumer, definition, 2L, false), configured);
        await ProjectionPublicAssertions.ExactAsync(configured, (await McpCallerAssertions.SuccessAsync<ProjectionConsumerInfo>(
            await caller.Mcp.CallAsync(McpCallerTools.ProjectionsConfigure, configure, token))).Value);
        var source = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new PutDocument(ProjectionPublicProtocol.Source, "next", ProjectionPublicProtocol.InputJson, 0L)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(source, token));
        await ProjectionPublicAssertions.LiteralReceiptAsync(source.CommandId, receipt,
            new MutationReceipt(ProjectionPublicProtocol.PutKind, ProjectionPublicProtocol.Source, "next", 1L));
        var batch = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReadProjectionAsync(new(consumer), token));
        await global::TUnit.Assertions.Assert.That(batch.Entries.Length).IsEqualTo(1);
        var original = batch.Entries.Single();
        var expected = new OutboxEntry(3L, 0, receipt.Token, original.CommittedAt, source.Mutations.Single(),
            receipt.Mutations.Single(), null, new(state.StoredSource("next"),
                1L, ProjectionPublicProtocol.InputJson, new RowAccess(), original.CommittedAt));
        await ProjectionPublicAssertions.ExactAsync(new ProjectionBatch(configured, [expected], 3L, batch.Token, false), batch);
        var effect = new CommitProjectionBatchRequest(Guid.NewGuid(), consumer, batch.Token,
            [new PutDocument(ProjectionPublicProtocol.Target, "healthy", ProjectionPublicProtocol.EffectJson, 0L)]);
        var completed = await ProjectionPublicOperations.CommitBothAsync(caller, effect, token);
        await ProjectionPublicAssertions.LiteralReceiptAsync(effect.CommandId, completed.Receipt,
            new MutationReceipt(ProjectionPublicProtocol.PutKind, ProjectionPublicProtocol.Target, "healthy", 1L));
        await ProjectionPublicAssertions.ExactAsync(new ProjectionBatchResult(completed.Receipt, false, 3L), completed);
        var tail = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReadProjectionAsync(new(consumer), token));
        await ProjectionPublicAssertions.ExactAsync(new ProjectionBatch(configured with
        {
            Checkpoint = 3L
        }, [], 4L, tail.Token, false), tail);
        var advance = new CommitProjectionBatchRequest(Guid.NewGuid(), consumer, tail.Token, []);
        var advanced = await ProjectionPublicOperations.CommitBothAsync(caller, advance, token);
        await ProjectionPublicAssertions.LiteralReceiptAsync(advance.CommandId, advanced.Receipt);
        await ProjectionPublicAssertions.ExactAsync(new ProjectionBatchResult(advanced.Receipt, false, 4L), advanced);
        var status = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(state.Partition, token));
        await global::TUnit.Assertions.Assert.That(status.Head.StoredBytes).IsGreaterThan(0L);
        await ProjectionPublicAssertions.ExactAsync(new OutboxHead(4L, 3L, 2L, status.Head.StoredBytes), status.Head);
        await ProjectionPublicAssertions.ExactAsync(global::System.Collections.Immutable.ImmutableArray.Create(
            new ProjectionConsumerInfo(state.Consumer, state.Definition, 2L, true),
            new ProjectionConsumerInfo(consumer, definition, 4L, false)), status.Consumers);
        await ProjectionPublicAssertions.ExactAsync(status, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await root.Mcp.CallAsync(McpCallerTools.OutboxStatus, state.Partition, token))).Value);
        var document = new GetDocumentRequest(new(state.Partition, ProjectionPublicProtocol.Target, "healthy"), completed.Receipt.Token);
        var expectedDocument = new DocumentReadResult(new(state.StoredTarget("healthy"), 1L, ProjectionPublicProtocol.EffectJson, false, []));
        await ProjectionPublicAssertions.ExactAsync(expectedDocument,
            await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.GetAsync(document.Reference, completed.Receipt.Token, token)));
        await ProjectionPublicAssertions.ExactAsync(expectedDocument, (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
            await root.Mcp.CallAsync(McpCallerTools.DocumentsGet, document, token))).Value);
    }
}
