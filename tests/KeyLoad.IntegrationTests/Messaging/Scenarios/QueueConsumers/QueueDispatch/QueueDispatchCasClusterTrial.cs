using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueDispatchCasClusterTrial
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var subject = QueueDispatchCasClusterProtocol.SubjectPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(new QueueDeadlineClusterSelection(subject));
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
                var state = await QueueDispatchCasClusterSeed.RunAsync(fixture, subject, lifetime.Token);
                await QueueDispatchCasClusterFlow.RunAsync(fixture, state, lifetime.Token);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
