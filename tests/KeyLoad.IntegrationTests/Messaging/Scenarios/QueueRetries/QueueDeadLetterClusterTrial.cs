using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadLetterClusterTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, bool mcpFirst)
    {
        using var deadline = McpCallerDeadline.Create();
        var failures = new List<Exception>();
        RequestCqrsClusterCallers? callers = null;
        QueueDeadLetterClusterState? state = null;
        NodeStatus? before = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            callers = await RequestCqrsClusterCallers.ConnectAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, deadline.Token);
            state = await QueueDeadLetterClusterScenario.SeedAsync(callers, mcpFirst, deadline.Token);
            await QueueDeadLetterClusterAssertions.LiteralAsync(callers, state, deadline.Token);
            await QueueDeadLetterClusterAssertions.ReplayAsync(callers, state, deadline.Token);
            await QueueDeadLetterClusterAssertions.DeniedAsync(fixture, state, deadline.Token);
            await QueueDeadLetterClusterAssertions.LiteralAsync(callers, state, deadline.Token);
            await QueueDeadLetterClusterScenario.HealthyAsync(callers, state, QueueDeadLetterClusterProtocol.First, deadline.Token);
            before = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.StatusAsync(deadline.Token));
        }, failures);
        if (callers is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => callers.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await QueueDeadLetterClusterCold.RunAsync(fixture, state!, before!, deadline.Token);
    }
}
