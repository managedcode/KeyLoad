using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateAnalyticsCold
{
    private const long ContinuedGeneration = 4;

    internal static Task<FunctionalGateColdState> SeedAsync(FunctionalGateFixture fixture, FunctionalGateColdState state,
        CancellationToken token) => WithAdministratorAsync(fixture, async administrator =>
    {
        await FunctionalGateAnalyticsInventory.RequireAsync(state, seeded: false);
        var users = new List<FunctionalGateAnalyticsColdUser>();
        foreach (var user in state.UserAnalytics)
        {
            var stores = await WithUserAsync(fixture, administrator, state, user, fixture.NodeNames[0],
                context => FunctionalGateAnalyticsColdSeed.RunAsync(context, token), token);
            users.Add(user with
            {
                Stores = stores
            });
        }
        return state with
        {
            UserAnalytics = [.. users]
        };
    }, token);

    internal static Task<bool> VerifyAllNodesAsync(FunctionalGateFixture fixture, FunctionalGateColdState state,
        CancellationToken token) => WithAdministratorAsync(fixture, async administrator =>
    {
        await FunctionalGateAnalyticsInventory.RequireAsync(state, seeded: true);
        foreach (var user in state.UserAnalytics)
        {
            foreach (var node in fixture.NodeNames)
            {
                await WithUserAsync(fixture, administrator, state, user, node, async context =>
                {
                    foreach (var store in user.Stores)
                    {
                        await FunctionalGateAnalyticsColdReplay.VerifyAsync(context, store, token);
                    }
                    return true;
                }, token);
            }
        }
        return true;
    }, token);

    internal static Task<bool> VerifyAfterPolicyRepairAllNodesAsync(FunctionalGateFixture fixture,
        FunctionalGateColdState state, IReadOnlySet<string> repairedPrincipalIds, CancellationToken token)
        => WithAdministratorAsync(fixture, async administrator =>
        {
            ArgumentNullException.ThrowIfNull(repairedPrincipalIds);
            await FunctionalGateAnalyticsInventory.RequireAsync(state, seeded: true);
            foreach (var user in state.UserAnalytics)
            {
                foreach (var node in fixture.NodeNames)
                {
                    await WithUserAsync(fixture, administrator, state, user, node, async context =>
                    {
                        foreach (var store in user.Stores)
                        {
                            if (repairedPrincipalIds.Contains(user.Identity.Principal.Id))
                            {
                                await FunctionalGateAnalyticsColdReplay.RepairedAsync(context, store, token);
                            }
                            else
                            {
                                await FunctionalGateAnalyticsColdReplay.VerifyAsync(context, store, token);
                            }
                        }
                        return true;
                    }, token);
                }
            }
            return true;
        }, token);

    internal static Task<bool> ContinueAsync(FunctionalGateFixture fixture, FunctionalGateColdState state,
        CancellationToken token) => WithAdministratorAsync(fixture, async administrator =>
    {
        await FunctionalGateAnalyticsInventory.RequireAsync(state, seeded: true);
        foreach (var user in state.UserAnalytics)
        {
            await WithUserAsync(fixture, administrator, state, user, fixture.NodeNames[^1], async context =>
            {
                foreach (var store in user.Stores)
                {
                    await FunctionalGateAnalyticsColdReplay.ContinueAsync(context, store, token);
                }
                await FunctionalGateAnalyticsPolicy.RunAsync(context, user.Identity.Principal,
                    user.Stores.ToDictionary(store => store.State.Store, _ => ContinuedGeneration, StringComparer.Ordinal), token);
                return true;
            }, token);
        }
        return true;
    }, token);

    private static Task<T> WithAdministratorAsync<T>(FunctionalGateFixture fixture,
        Func<RequestCqrsClusterCallers, Task<T>> action, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, fixture.NodeNames[0], fixture.AdminKey, action, token);

    private static Task<T> WithUserAsync<T>(FunctionalGateFixture fixture, RequestCqrsClusterCallers administrator,
        FunctionalGateColdState state, FunctionalGateAnalyticsColdUser user, string node,
        Func<FunctionalGateContext, Task<T>> action, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, node, user.Identity.Secret,
            caller => action(new(administrator, caller, caller, state.Partition, state.Anchor, FunctionalGateColdProtocol.Resources)
            {
                WriterPrincipalId = user.Identity.Principal.Id,
                ReaderPrincipalId = user.Identity.Principal.Id,
                ReferenceBindings = state.ReferenceBindings,
                ReplicaCount = fixture.ReplicaCount
            }), token);
}
