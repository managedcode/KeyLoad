using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadLetterRf3Trial
{
    internal static async Task RunAsync(ClusterFixture fixture, bool mcpFirst)
    {
        using var deadline = McpCallerDeadline.Create();
        var failures = new List<Exception>();
        RequestCqrsRf3Callers? callers = null;
        QueueDeadLetterRf3State? state = null;
        NodeStatus? before = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            callers = await RequestCqrsRf3Callers.ConnectAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, deadline.Token);
            state = await QueueDeadLetterRf3Scenario.SeedAsync(callers, mcpFirst, deadline.Token);
            await QueueDeadLetterRf3Assertions.LiteralAsync(callers, state, deadline.Token);
            await QueueDeadLetterRf3Assertions.ReplayAsync(callers, state, deadline.Token);
            await QueueDeadLetterRf3Assertions.DeniedAsync(fixture, state, deadline.Token);
            await QueueDeadLetterRf3Assertions.LiteralAsync(callers, state, deadline.Token);
            await QueueDeadLetterRf3Scenario.HealthyAsync(callers, state, QueueDeadLetterRf3Protocol.First, deadline.Token);
            before = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.StatusAsync(deadline.Token));
        }, failures);
        if (callers is not null)
        { await ServerFailureObserver.ObserveAsync(() => callers.DisposeAsync().AsTask(), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        await QueueDeadLetterRf3Cold.RunAsync(fixture, state!, before!, deadline.Token);
    }
}
