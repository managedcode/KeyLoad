using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryCapabilityVersionRf3Current
{
    internal static async Task ObserveAsync(RequestCqrsRf3Callers root, QueryCapabilityVersionRf3State state, Func<Task> action, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(action, failures);
        await ServerFailureObserver.ObserveAsync(() => CheckAsync(root, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task CheckAsync(RequestCqrsRf3Callers root, QueryCapabilityVersionRf3State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => ImageAsync(root, state, token), failures);
        await ServerFailureObserver.ObserveAsync(() => ReceiptAsync(root, state, token), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlRf3Protocol.EqualAsync(state.Receipt, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(state.Seed, token)));
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => ImageAsync(root, state, token), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await SqlRf3Protocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Seed, token))).Value);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => ImageAsync(root, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ImageAsync(RequestCqrsRf3Callers root, QueryCapabilityVersionRf3State state, CancellationToken token)
    {
        foreach (var input in state.Seed.Mutations.Cast<PutDocument>())
        {
            var reference = new EntityRef(state.Partition, input.Collection, input.Id);
            var expected = new DocumentReadResult(new(reference, 1L, input.Json, false, []));
            await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.GetAsync(reference, token)));
            await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
                await root.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value);
        }
        await SqlRf3Protocol.EqualAsync(state.Outbox, await QueryCursorRequestIdentityR47Current.OutboxAsync(root, state.Partition, token));
    }

    private static async Task ReceiptAsync(RequestCqrsRf3Callers root, QueryCapabilityVersionRf3State state, CancellationToken token)
    {
        var receipt = state.Receipt;
        var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(new(1, state.Partition), token));
        await Assert.That(receipt.CommandId).IsEqualTo(state.Seed.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(state.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(placement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(new MutationReceipt[]
            { new("putDocument", QueryCapabilityVersionRf3Requests.Collection, "a", 1L), new("putDocument", QueryCapabilityVersionRf3Requests.Collection, "b", 1L),
              new("putDocument", QueryCapabilityVersionRf3Requests.Collection, "c", 1L), new("putDocument", QueryCapabilityVersionRf3Requests.Collection, "d", 1L) }, receipt.Mutations.ToArray());
    }
}
