using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineLeaderClusterFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var subject = QueueDeadlineLeaderClusterProtocol.SubjectPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(new QueueDeadlineClusterSelection(subject));
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
                var state = await QueueDeadlineLeaderClusterPreparation.CreateAsync(fixture, subject, lifetime.Token);
                var ownership = await QueueDeadlineLeaderClusterOwnership.CaptureAsync(fixture, lifetime.Token);
                await QueueDeadlineLeaderClusterContinuation.LoseLeaderAsync(fixture, state, ownership, lifetime.Token);
                await QueueDeadlineLeaderClusterContinuation.RejoinAndColdAsync(fixture, state, ownership, lifetime.Token);
                await SecondColdAsync(fixture, state, ownership, lifetime.Token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task SecondColdAsync(ClusterFixture fixture, QueueDeadlineLeaderClusterState state,
        QueueDeadlineLeaderClusterOwnership ownership, CancellationToken token)
    {
        await QueueLeaseClusterCold.RestartAsync(fixture, token);
        await QueueDeadlineClusterCallers.UseAsync(fixture, ownership.OriginalLeaderNode, fixture.AdminKey,
            admin => QueueDeadlineClusterCallers.UseAsync(fixture, ownership.SurvivorNode, state.Identity.Secret,
                async worker =>
                {
                    await QueueDeadlineLeaderClusterCurrent.ObserveAsync(admin, state, token);
                    await QueueDeadlineLeaderClusterActions.StaleAsync(admin, state, token);
                    await QueueDeadlineLeaderClusterActions.ReplayAsync(admin, worker, state, token);
                    return true;
                }, token), token);
    }
}
