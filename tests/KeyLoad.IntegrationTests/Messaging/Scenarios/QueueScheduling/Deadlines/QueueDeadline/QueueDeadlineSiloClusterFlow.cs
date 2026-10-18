using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineSiloClusterFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var subject = QueueDeadlineSiloClusterProtocol.SubjectPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(new QueueDeadlineClusterSelection(subject));
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
                var state = await QueueDeadlineSiloClusterPreparation.CreateAsync(fixture, subject, lifetime.Token);
                var ownership = await QueueDeadlineSiloClusterOwnership.CaptureAsync(fixture, lifetime.Token);
                await QueueDeadlineSiloClusterContinuation.StopSiloAsync(fixture, state, ownership, lifetime.Token);
                await QueueDeadlineSiloClusterContinuation.RejoinAndColdAsync(fixture, state, ownership, lifetime.Token);
                await SecondColdAsync(fixture, state, ownership, lifetime.Token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task SecondColdAsync(ClusterFixture fixture, QueueDeadlineSiloClusterState state,
        QueueDeadlineSiloClusterOwnership ownership, CancellationToken token)
    {
        await QueueLeaseClusterCold.RestartAsync(fixture, token);
        await QueueDeadlineClusterCallers.UseAsync(fixture, ownership.OriginalSiloNode, fixture.AdminKey,
            admin => QueueDeadlineClusterCallers.UseAsync(fixture, ownership.SurvivorNode, state.Identity.Secret,
                async worker =>
                {
                    await QueueDeadlineSiloClusterCurrent.ObserveAsync(admin, state, token);
                    await QueueDeadlineSiloClusterActions.StaleAsync(admin, state, token);
                    await QueueDeadlineSiloClusterActions.ReplayAsync(admin, worker, state, token);
                    return true;
                }, token), token);
    }
}
