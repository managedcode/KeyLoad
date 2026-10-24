using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.ChangeFeeds;

internal static class ProjectionPublicWarm
{
    internal static async Task<ProjectionPublicState> RunAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers caller,
        PartitionRef partition, McpPersistedIdentity identity, EntityRefLifetime sourceLifetime,
        EntityRefLifetime targetLifetime, CancellationToken token)
    {
        var source = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(ProjectionPublicProtocol.Source, "input", ProjectionPublicProtocol.InputJson, 0L)]);
        var sourceReceipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(source, token));
        await ProjectionPublicAssertions.LiteralReceiptAsync(source.CommandId, sourceReceipt,
            new MutationReceipt(ProjectionPublicProtocol.PutKind, ProjectionPublicProtocol.Source, "input", 1L));
        var consumer = new ProjectionConsumerRef(partition, ProjectionPublicProtocol.Consumer);
        var definition = new ProjectionConsumerDefinition(1L, [ProjectionPublicProtocol.Source], [ProjectionPublicProtocol.PutKind]);
        var configure = new ConfigureProjectionConsumerRequest(Guid.NewGuid(), consumer, definition, 0L);
        var configured = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ConfigureProjectionAsync(configure, token));
        await ProjectionPublicAssertions.ExactAsync(new ProjectionConsumerInfo(consumer, definition, 0L, false), configured);
        await ProjectionPublicAssertions.ExactAsync(configured, (await McpCallerAssertions.SuccessAsync<ProjectionConsumerInfo>(
            await caller.Mcp.CallAsync(McpCallerTools.ProjectionsConfigure, configure, token))).Value);
        var read = new ReadProjectionBatchRequest(consumer);
        var batch = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReadProjectionAsync(read, token));
        await ProjectionPublicAssertions.LiteralBatchAsync(batch, consumer, definition, sourceReceipt, sourceLifetime);
        var mcpBatch = (await McpCallerAssertions.SuccessAsync<ProjectionBatch>(
            await caller.Mcp.CallAsync(McpCallerTools.ProjectionsRead, read, token))).Value;
        await ProjectionPublicAssertions.ExactAsync(batch with
        {
            Token = mcpBatch.Token
        }, mcpBatch);
        var complete = new CommitProjectionBatchRequest(Guid.NewGuid(), consumer, batch.Token,
            [new PutDocument(ProjectionPublicProtocol.Target, "effect", ProjectionPublicProtocol.EffectJson, 0L)]);
        var completed = await ProjectionPublicOperations.CommitBothAsync(caller, complete, token);
        await ProjectionPublicAssertions.LiteralReceiptAsync(complete.CommandId, completed.Receipt,
            new MutationReceipt(ProjectionPublicProtocol.PutKind, ProjectionPublicProtocol.Target, "effect", 1L));
        await ProjectionPublicAssertions.ExactAsync(new ProjectionBatchResult(completed.Receipt, false, 1L), completed);
        var next = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReadProjectionAsync(read, token));
        await ProjectionPublicAssertions.ExactAsync(new ProjectionBatch(configured with
        {
            Checkpoint = 1L
        }, [], 2L, next.Token, false), next);
        var filtered = new CommitProjectionBatchRequest(Guid.NewGuid(), consumer, next.Token, []);
        var advanced = await ProjectionPublicOperations.CommitBothAsync(caller, filtered, token);
        await ProjectionPublicAssertions.LiteralReceiptAsync(filtered.CommandId, advanced.Receipt);
        await ProjectionPublicAssertions.ExactAsync(new ProjectionBatchResult(advanced.Receipt, false, 2L), advanced);
        return new(partition, identity, source, sourceReceipt, consumer, definition, configure,
            configured, batch, complete, completed, filtered, advanced, sourceLifetime, targetLifetime);
    }
}
