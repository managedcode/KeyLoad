using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionGroupLeaderFailoverCallers
{
    private const int FirstSurvivor = 0;
    private const int SecondSurvivor = 1;
    private const long NoCommitPosition = 0;
    internal static async Task RunAsync(ClusterFixture fixture, SubscriptionIndependentCheckpointR52State state,
        string[] survivors, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var administratorHttp = McpCallerHttp.Create(fixture, survivors[FirstSurvivor]);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var administrator = new KeyLoadClient(administratorHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
                await using var official = await McpOfficialClient.ConnectAsync(fixture, survivors[SecondSurvivor], fixture.AdminKey, token);
                await ServerFailureObserver.ObserveAsync(
                    () => WorkerAsync(fixture, state, survivors, administrator, official, token), failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WorkerAsync(ClusterFixture fixture, SubscriptionIndependentCheckpointR52State state,
        string[] survivors, KeyLoadClient administrator, McpOfficialClient official, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var workerHttp = McpCallerHttp.Create(fixture, survivors[SecondSurvivor]);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var sdk = new KeyLoadClient(workerHttp, state.Seed.Manager.Secret, IntegrationClientOptions.Execution());
                await using var mcp = await McpOfficialClient.ConnectAsync(fixture, survivors[FirstSurvivor], state.Seed.Manager.Secret, token);
                await ServerFailureObserver.ObserveAsync(
                    () => ProgressAsync(state, sdk, mcp, administrator, official, token), failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ProgressAsync(SubscriptionIndependentCheckpointR52State state, KeyLoadClient sdk,
        McpOfficialClient mcp, KeyLoadClient administrator, McpOfficialClient official, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, administrator, official, token);
        await SubscriptionIndependentCheckpointR52Replay.RunAsync(state, sdk, mcp, administrator, official, token);
        var request = SubscriptionPhasePublicAssertions.Receive(state.Seed.Independent);
        var empty = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveSubscriptionAsync(request, token));
        await Assert.That(empty.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(empty.Deliveries).IsEmpty();
        await SubscriptionFilterRf3Assertions.EqualAsync(empty.Status,
            SubscriptionIndependentCheckpointR52Current.Expected(state, state.Seed.Independent));
        await Assert.That(empty.Token.AtomicPartitionId).IsEqualTo(state.Seed.Independent.Source.Partition.AtomicPartitionId);
        await Assert.That(empty.Token.Incarnation).IsEqualTo(state.Placement!.Incarnation);
        await Assert.That(empty.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
        await Assert.That(empty.Token.Position).IsGreaterThan(NoCommitPosition);
        await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, administrator, official, token);
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveSubscriptionResult>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsReceive, request, token))).Value, empty);
        await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, administrator, official, token);
        await SubscriptionIndependentCheckpointR52Replay.RunAsync(state, sdk, mcp, administrator, official, token);
        await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, administrator, official, token);
    }
}
