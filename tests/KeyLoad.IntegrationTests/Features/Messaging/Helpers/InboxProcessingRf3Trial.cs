using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class InboxProcessingRf3Trial
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
                var administrator = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
                var seed = await InboxProcessingRf3Seed.CreateAsync(fixture, administrator, deadline.Token);
                using var workerHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
                var worker = new KeyLoadClient(workerHttp, seed.Identity.Secret, IntegrationClientOptions.Execution());
                var state = await InboxProcessingRf3Original.ExecuteAsync(worker, seed.Lane, seed.Identity, deadline.Token);
                await InboxProcessingRf3Cold.RestartAsync(fixture, deadline.Token);
                await ReconcileAndRestoreAsync(fixture, worker, state, deadline.Token);
                await InboxProcessingRf3Cold.RestartAsync(fixture, deadline.Token);
                await CompleteAsync(fixture, worker, state, deadline.Token);
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static Task ReconcileAndRestoreAsync(ClusterFixture fixture, KeyLoadClient worker,
        InboxProcessingRf3State state, CancellationToken token)
        => WithMcpAsync(fixture, state, async mcp =>
        {
            await InboxProcessingRf3Replay.ExecuteAsync(worker, mcp, state, true, token);
            await InboxProcessingRf3Policy.ExecuteAsync(fixture, worker, mcp, state, token);
        }, token);

    private static Task CompleteAsync(ClusterFixture fixture, KeyLoadClient worker,
        InboxProcessingRf3State state, CancellationToken token)
        => WithMcpAsync(fixture, state, async mcp =>
        {
            await InboxProcessingRf3Replay.ExecuteAsync(worker, mcp, state, false, token);
            var outputLane = new QueueLaneRef(state.Lane.Partition, InboxProcessingRf3Protocol.Output);
            var next = await McpCallerAssertions.SdkSuccessAsync(await worker.ReceiveAsync(new(Guid.NewGuid(), outputLane), token));
            var delivery = await Assert.That(next.Deliveries).HasSingleItem();
            await Assert.That(delivery.PayloadJson).IsEqualTo(InboxProcessingRf3Protocol.Payload);
            _ = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(McpCallerTools.MessagesProcess,
                new ProcessingRequest(Guid.NewGuid(), outputLane, delivery.Token, InboxProcessingRf3Protocol.Handler,
                    InboxProcessingRf3Protocol.Generation, []), token));
            var completed = await McpCallerAssertions.SdkSuccessAsync(await worker.InspectAsync(
                new(outputLane, InboxProcessingRf3Protocol.Message), token));
            await Assert.That(completed!.Metadata.State).IsEqualTo(MessageState.Acked);
        }, token);

    private static async Task WithMcpAsync(ClusterFixture fixture, InboxProcessingRf3State state,
        Func<McpOfficialClient, Task> execute, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, state.Identity.Secret, token);
            await ServerFailureObserver.ObserveAsync(() => execute(mcp), failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
