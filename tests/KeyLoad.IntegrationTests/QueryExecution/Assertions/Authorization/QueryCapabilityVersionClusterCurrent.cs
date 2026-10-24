using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class QueryCapabilityVersionClusterCurrent
{
    internal static async Task ObserveAsync(RequestCqrsClusterCallers root, QueryCapabilityVersionClusterState state, Func<Task> action, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(action, failures);
        await ServerFailureObserver.ObserveAsync(() => CheckAsync(root, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task CheckAsync(RequestCqrsClusterCallers root, QueryCapabilityVersionClusterState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => ImageAsync(root, state, token), failures);
        await ServerFailureObserver.ObserveAsync(() => ReceiptAsync(state), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlClusterProtocol.EqualAsync(state.Receipt, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(state.Seed, token)));
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => ImageAsync(root, state, token), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlClusterProtocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Seed, token))).Value);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => ImageAsync(root, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ImageAsync(RequestCqrsClusterCallers root, QueryCapabilityVersionClusterState state, CancellationToken token)
    {
        foreach (var input in state.Seed.Mutations.Cast<PutDocument>())
        {
            var reference = new EntityRef(state.Partition, input.Collection, input.Id);
            var expected = new DocumentReadResult(new(state.Stored(input.Id), 1L, input.Json, false, []));
            await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.GetAsync(reference, token)));
            await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
                await root.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value);
        }
        await SqlClusterProtocol.EqualAsync(state.Outbox, await QueryCursorRequestIdentityR47Current.OutboxAsync(root, state.Partition, token));
    }

    private static async Task ReceiptAsync(QueryCapabilityVersionClusterState state)
    {
        var receipt = state.Receipt;
        await Assert.That(receipt.CommandId).IsEqualTo(state.Seed.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(state.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        await SqlClusterProtocol.EqualAsync(new MutationReceipt[]
            { new("putDocument", QueryCapabilityVersionClusterRequests.Collection, "a", 1L), new("putDocument", QueryCapabilityVersionClusterRequests.Collection, "b", 1L),
              new("putDocument", QueryCapabilityVersionClusterRequests.Collection, "c", 1L), new("putDocument", QueryCapabilityVersionClusterRequests.Collection, "d", 1L) }, receipt.Mutations.ToArray());
    }
}
