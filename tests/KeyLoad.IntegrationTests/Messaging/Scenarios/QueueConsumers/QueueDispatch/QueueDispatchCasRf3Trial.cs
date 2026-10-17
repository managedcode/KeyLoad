using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDispatchCasRf3Trial
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var subject = QueueDispatchCasRf3Protocol.SubjectPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(new QueueDeadlineRf3Selection(subject));
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
                var state = await QueueDispatchCasRf3Seed.RunAsync(fixture, subject, lifetime.Token);
                await QueueDispatchCasRf3Flow.RunAsync(fixture, state, lifetime.Token);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
