using KeyLoad.IntegrationTests.OrleansExecution;
namespace KeyLoad.IntegrationTests.Messaging;

internal static class CompetingInboxPublicCallers
{
    internal static async Task WithRootAsync(CompetingInboxPublicState state,
        Func<RequestCqrsClusterCallers, Task> operation, CancellationToken token)
        => _ = await RequestCqrsClusterCallers.RunOwnedAsync(state.Fixture.App, RequestCqrsClusterProtocol.Node1,
            state.Fixture.AdminKey, async root =>
            {
                await operation(root);
                return true;
            }, token);
    internal static async Task WithAsync(CompetingInboxPublicState state,
        Func<RequestCqrsClusterCallers, RequestCqrsClusterCallers, RequestCqrsClusterCallers, Task> operation, CancellationToken token)
        => await WithRootAsync(state, async root =>
        {
            _ = await RequestCqrsClusterCallers.RunOwnedAsync(state.Fixture.App, RequestCqrsClusterProtocol.Node2,
                state.Identity.Secret, async sdk =>
                {
                    _ = await RequestCqrsClusterCallers.RunOwnedAsync(state.Fixture.App, RequestCqrsClusterProtocol.Node3,
                        state.Identity.Secret, async official =>
                        {
                            await operation(root, sdk, official);
                            return true;
                        }, token);
                    return true;
                }, token);
        }, token);
}
