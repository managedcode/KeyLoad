using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class AtomicTriadStagedClusterWarm
{
    internal static async Task RunAsync(ClusterFixture fixture, RequestCqrsClusterCallers root, RequestCqrsClusterCallers caller,
        AtomicTriadStagedClusterState state, List<Exception> failures, CancellationToken token)
    {
        await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedClusterReplies.RefusedAsync(root, caller, state, state.Failed,
            ErrorCode.Conflict, AtomicTriadStagedClusterProtocol.DuplicateDetail, AtomicTriadStagedClusterProtocol.GenericDetail, token), failures);
        await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedClusterSetup.PolicyAsync(fixture, state,
            AtomicTriadStagedClusterProtocol.WithdrawnEpoch, false, token), failures);
        await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedClusterReplies.RefusedAsync(root, caller, state,
            AtomicTriadStagedClusterRecipe.Command(state.Partition, false), ErrorCode.PermissionDenied,
            AtomicTriadStagedClusterProtocol.ScopeDetail, AtomicTriadStagedClusterProtocol.ScopeDetail, token), failures);
        await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedClusterSetup.PolicyAsync(fixture, state,
            AtomicTriadStagedClusterProtocol.RepairedEpoch, true, token), failures);
        await ServerFailureObserver.ObserveAsync(() => ProduceAsync(caller, state, token), failures);
        await ServerFailureObserver.ObserveAsync(() => CaptureStreamAsync(caller, state, token), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            state.Outbox = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(state.Partition, token));
            await AtomicProducerClusterOutbox.LiteralAsync(state.Outbox, AtomicTriadStagedClusterProtocol.MutationCount);
        }, failures);
    }
    private static async Task ProduceAsync(RequestCqrsClusterCallers caller,
        AtomicTriadStagedClusterState state, CancellationToken token)
    {
        state.Healthy = AtomicTriadStagedClusterRecipe.Command(state.Partition, false);
        var result = await caller.Sdk.CommitAsync(state.Healthy, token);
        state.Returned.Add(state.Healthy.CommandId);
        state.HealthyCommitted = result.IsSuccess;
        var receipt = await McpCallerAssertions.SdkSuccessAsync(result);
        state.Receipt = new(state.Healthy.CommandId, new(receipt.Token.Incarnation, state.Partition.AtomicPartitionId,
            receipt.Token.Position), AtomicTriadStagedClusterRecipe.Receipts, DurabilityProfile.ProcessDurable, 3);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(receipt.Token.Position > 0L).IsTrue(), failures);
        await ServerFailureObserver.ObserveAsync(() => QueueProducerClusterAssertions.EqualAsync(receipt, state.Receipt), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task CaptureStreamAsync(RequestCqrsClusterCallers caller, AtomicTriadStagedClusterState state, CancellationToken token)
    {
        var page = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReadStreamAsync(new(new(state.Partition,
            AtomicTriadStagedClusterProtocol.Streams, AtomicTriadStagedClusterProtocol.Entity)), token));
        if (page.Events.Length > 0)
        {
            state.CommittedAt = page.Events[0].RecordedAt;
        }
        await Assert.That(page.Events.Length).IsEqualTo(2);
    }
}
