using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class AtomicTriadStagedClusterFlow
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                AtomicTriadStagedClusterState state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                        root => AtomicTriadStagedClusterSetup.RunAsync(fixture, root, deadline.Token), deadline.Token);
                }
                var coldOwner = new AtomicTriadStagedClusterCold();
                await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, state, true), failures);
                for (var cold = 0; cold < AtomicTriadStagedClusterProtocol.ColdCycles; cold++)
                {
                    using var deadline = McpCallerDeadline.Create();
                    await ServerFailureObserver.ObserveAsync(() => coldOwner.RestartAsync(fixture, deadline.Token), failures);
                    await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, state, false), failures);
                    await ServerFailureObserver.ObserveAsync(() => coldOwner.RequireAsync(deadline.Token), failures);
                }
                using var final = McpCallerDeadline.Create();
                await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, state, false), failures);
                await ServerFailureObserver.ObserveAsync(() => coldOwner.RequireAsync(final.Token), failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task WaveAsync(ClusterFixture fixture, AtomicTriadStagedClusterState state, bool warm)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async caller =>
            {
                var failures = new List<Exception>();
                if (warm)
                {
                    await AtomicTriadStagedClusterWarm.RunAsync(fixture, root, caller, state, failures, deadline.Token);
                }
                await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedClusterCurrent.RunAsync(root, caller, state, deadline.Token), failures);
                await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedClusterReplies.ReplayAsync(root, caller, state, deadline.Token), failures);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    if (!state.Returned.Contains(state.Failed.CommandId))
                    {
                        throw new InvalidOperationException(AtomicTriadStagedClusterProtocol.Missing);
                    }
                    await AtomicTriadStagedClusterReplies.RefusedAsync(root, caller, state, state.Failed, ErrorCode.PermissionDenied,
                        AtomicTriadStagedClusterProtocol.PolicyDetail, AtomicTriadStagedClusterProtocol.PermissionDetail, deadline.Token);
                }, failures);
                ServerFailureObserver.ThrowIfAny(failures);
                return true;
            }, deadline.Token), deadline.Token);
    }
}
