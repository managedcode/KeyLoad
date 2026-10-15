using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class AtomicTriadStagedRf3Warm
{
    internal static async Task RunAsync(ClusterFixture fixture, RequestCqrsRf3Callers root, RequestCqrsRf3Callers caller,
        AtomicTriadStagedRf3State state, List<Exception> failures, CancellationToken token)
    {
        await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedRf3Replies.RefusedAsync(root, caller, state, state.Failed,
            ErrorCode.Conflict, AtomicTriadStagedRf3Protocol.DuplicateDetail, AtomicTriadStagedRf3Protocol.GenericDetail, token), failures);
        await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedRf3Setup.PolicyAsync(fixture, state,
            AtomicTriadStagedRf3Protocol.WithdrawnEpoch, false, token), failures);
        await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedRf3Replies.RefusedAsync(root, caller, state,
            AtomicTriadStagedRf3Recipe.Command(state.Partition, false), ErrorCode.PermissionDenied,
            AtomicTriadStagedRf3Protocol.ScopeDetail, AtomicTriadStagedRf3Protocol.ScopeDetail, token), failures);
        await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedRf3Setup.PolicyAsync(fixture, state,
            AtomicTriadStagedRf3Protocol.RepairedEpoch, true, token), failures);
        await ServerFailureObserver.ObserveAsync(() => ProduceAsync(root, caller, state, token), failures);
        await ServerFailureObserver.ObserveAsync(() => CaptureStreamAsync(caller, state, token), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            state.Outbox = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(state.Partition, token));
            await AtomicProducerRf3Outbox.LiteralAsync(state.Outbox, AtomicTriadStagedRf3Protocol.MutationCount);
        }, failures);
    }
    private static async Task ProduceAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers caller,
        AtomicTriadStagedRf3State state, CancellationToken token)
    {
        state.Healthy = AtomicTriadStagedRf3Recipe.Command(state.Partition, false);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(
            new(AtomicTriadStagedRf3Protocol.PlacementVersion, state.Partition), token));
        var result = await caller.Sdk.CommitAsync(state.Healthy, token);
        state.Returned.Add(state.Healthy.CommandId);
        state.HealthyCommitted = result.IsSuccess && result.Value is not null;
        var receipt = await McpCallerAssertions.SdkSuccessAsync(result);
        state.Receipt = new(state.Healthy.CommandId, new(placement.Incarnation, state.Partition.AtomicPartitionId,
            receipt.Token.Position, placement.PlacementEpoch), AtomicTriadStagedRf3Recipe.Receipts, DurabilityProfile.QuorumProcessDurable);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(receipt.Token.Position > 0L).IsTrue(), failures);
        await ServerFailureObserver.ObserveAsync(() => QueueProducerRf3Assertions.EqualAsync(receipt, state.Receipt), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task CaptureStreamAsync(RequestCqrsRf3Callers caller, AtomicTriadStagedRf3State state, CancellationToken token)
    {
        var page = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReadStreamAsync(new(new(state.Partition,
            AtomicTriadStagedRf3Protocol.Streams, AtomicTriadStagedRf3Protocol.Entity)), token));
        if (page.Events.Length > 0)
        {
            state.CommittedAt = page.Events[0].RecordedAt;
        }
        await Assert.That(page.Events.Length).IsEqualTo(2);
    }
}
