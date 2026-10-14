using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineLeaderRf3Flow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var subject = QueueDeadlineLeaderRf3Protocol.SubjectPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(new QueueDeadlineRf3Selection(subject));
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
                var state = await QueueDeadlineLeaderRf3Preparation.CreateAsync(fixture, subject, lifetime.Token);
                var ownership = await QueueDeadlineLeaderRf3Ownership.CaptureAsync(fixture, lifetime.Token);
                await QueueDeadlineLeaderRf3Continuation.LoseLeaderAsync(fixture, state, ownership, lifetime.Token);
                await QueueDeadlineLeaderRf3Continuation.RejoinAndColdAsync(fixture, state, ownership, lifetime.Token);
                await SecondColdAsync(fixture, state, ownership, lifetime.Token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task SecondColdAsync(ClusterFixture fixture, QueueDeadlineLeaderRf3State state,
        QueueDeadlineLeaderRf3Ownership ownership, CancellationToken token)
    {
        await QueueLeaseRf3Cold.RestartAsync(fixture, token);
        await QueueDeadlineRf3Callers.UseAsync(fixture, ownership.OriginalLeaderNode, fixture.AdminKey,
            admin => QueueDeadlineRf3Callers.UseAsync(fixture, ownership.SurvivorNode, state.Identity.Secret,
                async worker =>
                {
                    await QueueDeadlineLeaderRf3Current.ObserveAsync(admin, state, token);
                    await QueueDeadlineLeaderRf3Actions.StaleAsync(admin, state, token);
                    await QueueDeadlineLeaderRf3Actions.ReplayAsync(admin, worker, state, token);
                    return true;
                }, token), token);
    }
}
