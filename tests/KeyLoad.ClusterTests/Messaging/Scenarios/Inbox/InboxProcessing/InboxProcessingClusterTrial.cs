using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.Server;
namespace KeyLoad.ClusterTests.Messaging;

internal static class InboxProcessingClusterTrial
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
                var seed = await InboxProcessingClusterSeed.CreateAsync(fixture, administrator, deadline.Token);
                using var workerHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
                var worker = new KeyLoadClient(workerHttp, seed.Identity.Secret, IntegrationClientOptions.Execution());
                var state = await InboxProcessingClusterOriginal.ExecuteAsync(worker, seed.Lane, seed.Identity, deadline.Token);
                var late = await LocalProcessingLateR53Warm.RunAsync(fixture, seed.Lane, seed.Identity, seed.CollectionLifetime, deadline.Token);
                await InboxProcessingClusterCold.RestartAsync(fixture, deadline.Token);
                await LocalProcessingLateR53Flow.FirstColdAsync(fixture, late, deadline.Token);
                await ReconcileAndRestoreAsync(fixture, worker, state, deadline.Token);
                await LocalProcessingLateR53Flow.RestoredAsync(fixture, late, deadline.Token);
                await InboxProcessingClusterCold.RestartAsync(fixture, deadline.Token);
                await CompleteAsync(fixture, worker, state, deadline.Token);
                await LocalProcessingLateR53Flow.FinishAsync(fixture, late, deadline.Token);
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static Task ReconcileAndRestoreAsync(ClusterFixture fixture, KeyLoadClient worker,
        InboxProcessingClusterState state, CancellationToken token)
        => WithMcpAsync(fixture, state, async mcp =>
        {
            await InboxProcessingClusterReplay.ExecuteAsync(worker, mcp, state, true, token);
            await InboxProcessingClusterPolicy.ExecuteAsync(fixture, worker, mcp, state, token);
        }, token);

    private static Task CompleteAsync(ClusterFixture fixture, KeyLoadClient worker,
        InboxProcessingClusterState state, CancellationToken token)
        => WithMcpAsync(fixture, state, async mcp =>
        {
            await InboxProcessingClusterReplay.ExecuteAsync(worker, mcp, state, false, token);
            var outputLane = new QueueLaneRef(state.Lane.Partition, InboxProcessingClusterProtocol.Output);
            var next = await McpCallerAssertions.SdkSuccessAsync(await worker.ReceiveAsync(new(Guid.NewGuid(), outputLane), token));
            var delivery = await Assert.That(next.Deliveries).HasSingleItem();
            await Assert.That(delivery.PayloadJson).IsEqualTo(InboxProcessingClusterProtocol.Payload);
            _ = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(McpCallerTools.MessagesProcess,
                new ProcessingRequest(Guid.NewGuid(), outputLane, delivery.Token, InboxProcessingClusterProtocol.Handler,
                    InboxProcessingClusterProtocol.Generation, []), token));
            var completed = await PublicReadAssertions.MessageAsync(await worker.InspectAsync(
                new(outputLane, InboxProcessingClusterProtocol.Message), token));
            await Assert.That(completed!.Metadata.State).IsEqualTo(MessageState.Acked);
        }, token);

    private static async Task WithMcpAsync(ClusterFixture fixture, InboxProcessingClusterState state,
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
