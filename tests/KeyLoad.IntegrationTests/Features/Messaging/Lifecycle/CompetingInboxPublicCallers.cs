using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class CompetingInboxPublicCallers
{
    internal static async Task WithRootAsync(CompetingInboxPublicState state,
        Func<RequestCqrsRf3Callers, Task> operation, CancellationToken token)
        => _ = await RequestCqrsRf3Callers.RunOwnedAsync(state.Fixture.App, RequestCqrsRf3Protocol.Node1,
            state.Fixture.AdminKey, async root =>
            {
                await operation(root);
                return true;
            }, token);
    internal static async Task WithAsync(CompetingInboxPublicState state,
        Func<RequestCqrsRf3Callers, RequestCqrsRf3Callers, RequestCqrsRf3Callers, Task> operation, CancellationToken token)
        => await WithRootAsync(state, async root =>
        {
            _ = await RequestCqrsRf3Callers.RunOwnedAsync(state.Fixture.App, RequestCqrsRf3Protocol.Node2,
                state.Identity.Secret, async sdk =>
                {
                    _ = await RequestCqrsRf3Callers.RunOwnedAsync(state.Fixture.App, RequestCqrsRf3Protocol.Node3,
                        state.Identity.Secret, async official =>
                        {
                            await operation(root, sdk, official);
                            return true;
                        }, token);
                    return true;
                }, token);
        }, token);
}
