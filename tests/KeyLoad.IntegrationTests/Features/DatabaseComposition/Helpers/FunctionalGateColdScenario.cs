using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateColdScenario
{
    internal static async Task RunAsync(int nodes, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(30), TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = await FunctionalGateFixture.StartColdAsync(nodes, lifetime.Token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, fixture.NodeNames[0], fixture.AdminKey,
                    administrator => FunctionalGateColdProvision.CreateAsync(administrator, lifetime.Token), lifetime.Token);
                state = await FunctionalGateColdCallers.RunAsync(fixture, state,
                    context => FunctionalGateColdSeed.RunAsync(context, state, lifetime.Token), lifetime.Token);
                state = await FunctionalGateColdWorkloads.SeedAsync(fixture, state, lifetime.Token);
                state = await FunctionalGateAnalyticsCold.SeedAsync(fixture, state, lifetime.Token);
                state = await FunctionalGateColdAnalytics.CaptureAsync(fixture, state, lifetime.Token);
                // All original SDK and MCP caller scopes have joined disposal before any native node kill.
                await FunctionalGateColdReopen.RunAsync(fixture, lifetime.Token);
                await FunctionalGateAnalyticsCold.VerifyAllNodesAsync(fixture, state, lifetime.Token);
                await FunctionalGateColdCallers.RunAsync(fixture, state, async context =>
                {
                    await FunctionalGateNodeAssertions.VerifyAsync(fixture, context, lifetime.Token);
                    await FunctionalGateColdReplay.RunAsync(context, state, lifetime.Token);
                    _ = await FunctionalGateColdConcurrent.RunAsync(fixture, context, state, seed: false, lifetime.Token);
                    await FunctionalGateColdWorkloads.VerifyAsync(fixture, context, state, lifetime.Token);
                    await FunctionalGateColdAuthorization.RunAsync(context, state, lifetime.Token);
                    await FunctionalGateColdAnalytics.VerifyAsync(fixture, state, lifetime.Token);
                    return true;
                }, lifetime.Token);
                var dropped = await FunctionalGateColdCallers.RunAsync(fixture, state,
                    context => FunctionalGateColdDrop.RunAsync(context, state, lifetime.Token), lifetime.Token);
                // The complete original caller scope has joined before the second same-root kill.
                await FunctionalGateColdReopen.RunAsync(fixture, lifetime.Token);
                var repairedPrincipalIds = state.AdditionalIdentities.Select(identity => identity.Principal.Id)
                    .Append(state.Writer.Principal.Id).ToHashSet(StringComparer.Ordinal);
                await FunctionalGateAnalyticsCold.VerifyAfterPolicyRepairAllNodesAsync(fixture, state,
                    repairedPrincipalIds, lifetime.Token);
                await FunctionalGateColdCallers.RunAsync(fixture, state, async context =>
                {
                    await FunctionalGateNodeAssertions.VerifyAsync(fixture, context, lifetime.Token);
                    await FunctionalGateColdDrop.VerifyAsync(context, state, dropped, lifetime.Token);
                    await FunctionalGateColdRetainedUsers.VerifyAsync(fixture, context, state,
                        caller => FunctionalGateColdCurrentModels.VerifyAsync(caller, state, dropped, lifetime.Token), lifetime.Token);
                    return true;
                }, lifetime.Token);
                await FunctionalGateAnalyticsCold.ContinueAsync(fixture, state, lifetime.Token);
            }, failures);
            fixture.PreserveEvidence = failures.Count != 0;
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
