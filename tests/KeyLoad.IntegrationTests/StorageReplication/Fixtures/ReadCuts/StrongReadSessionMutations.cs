using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;
using static KeyLoad.IntegrationTests.StorageReplication.StrongReadSessionProtocol;

namespace KeyLoad.IntegrationTests.StorageReplication;

internal static class StrongReadSessionMutations
{
    internal static async Task<StrongReadSessionState> ApplyAsync(RequestCqrsClusterCallers root,
        StrongReadSessionState state, bool delete, CancellationToken token)
    {
        Mutation mutation = delete ? new DeleteDocument(Collection, TargetId, CreatedRevision)
            : new PutDocument(Collection, TargetId, TargetJson);
        var command = new CommandRequest(Guid.NewGuid(), state.Partition, [mutation]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        var original = state.Commits.First().Receipt.Token;
        await Assert.That(receipt.Token.Position).IsGreaterThan(state.Commits.Last().Receipt.Token.Position);
        await SqlClusterProtocol.EqualAsync(new CommitReceipt(command.CommandId,
            new(original.Incarnation, state.Partition.AtomicPartitionId, receipt.Token.Position),
            [new(delete ? DeleteKind : PutKind, Collection, TargetId, delete ? DeletedRevision : CreatedRevision)],
            DurabilityProfile.ProcessDurable, 3), receipt);
        var next = state with
        {
            Commits = [.. state.Commits, new(command, receipt)],
            Target = delete ? null : new(new(state.Partition, Collection, TargetId), CreatedRevision, TargetJson, false, []),
            Outbox = await QueryCursorRequestIdentityR47Current.OutboxAsync(root, state.Partition, token)
        };
        await StrongReadSessionCurrent.CheckAsync(root, next, token);
        return next;
    }
    internal static async Task ReplayAsync(RequestCqrsClusterCallers root, StrongReadSessionState state, CancellationToken token)
    {
        foreach (var original in state.Commits)
        {
            await StrongReadSessionCurrent.ObserveAsync(root, state, async () =>
                await SqlClusterProtocol.EqualAsync(original.Receipt, await McpCallerAssertions.SdkSuccessAsync(
                    await root.Sdk.CommitAsync(original.Command, token))), token);
            await StrongReadSessionCurrent.ObserveAsync(root, state, async () =>
                await SqlClusterProtocol.EqualAsync(original.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                    await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, original.Command, token))).Value), token);
            var call = SqlClusterProtocol.Call(state.Partition, McpCallerTools.DocumentsCommit, original.Command);
            await StrongReadSessionCurrent.ObserveAsync(root, state, async () =>
                await SqlClusterProtocol.EqualAsync(original.Receipt,
                    await SqlClusterProtocol.SdkAsync<CommitReceipt>(root.Sdk, call, token)), token);
            await StrongReadSessionCurrent.ObserveAsync(root, state, async () =>
                await SqlClusterProtocol.EqualAsync(original.Receipt,
                    await SqlClusterProtocol.McpAsync<CommitReceipt>(root.Mcp, call, token)), token);
        }
    }
}
