using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class AtomicTriadStagedRf3Flow
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
                fixture.RegisterNativeCoverageCase<AtomicTriadStagedRf3Tests>(
                    nameof(AtomicTriadStagedRf3Tests.RepeatedTriadLateDuplicateThenSixReceiptsCurrentPrincipalAndTwoColdOwners));
                AtomicTriadStagedRf3State state;
                using (var deadline = McpCallerDeadline.Create())
                {
                    state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
                        root => AtomicTriadStagedRf3Setup.RunAsync(fixture, root, deadline.Token), deadline.Token);
                }
                var coldOwner = new AtomicTriadStagedRf3Cold();
                await ServerFailureObserver.ObserveAsync(() => WaveAsync(fixture, state, true), failures);
                for (var cold = 0; cold < AtomicTriadStagedRf3Protocol.ColdCycles; cold++)
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
    private static async Task WaveAsync(ClusterFixture fixture, AtomicTriadStagedRf3State state, bool warm)
    {
        using var deadline = McpCallerDeadline.Create();
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, root =>
            RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2, state.Identity.Secret, async caller =>
            {
                var failures = new List<Exception>();
                if (warm)
                {
                    await AtomicTriadStagedRf3Warm.RunAsync(fixture, root, caller, state, failures, deadline.Token);
                }
                await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedRf3Current.RunAsync(root, caller, state, deadline.Token), failures);
                await ServerFailureObserver.ObserveAsync(() => AtomicTriadStagedRf3Replies.ReplayAsync(root, caller, state, deadline.Token), failures);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    if (!state.Returned.Contains(state.Failed.CommandId))
                    {
                        throw new InvalidOperationException(AtomicTriadStagedRf3Protocol.Missing);
                    }
                    await AtomicTriadStagedRf3Replies.RefusedAsync(root, caller, state, state.Failed, ErrorCode.PermissionDenied,
                        AtomicTriadStagedRf3Protocol.PolicyDetail, AtomicTriadStagedRf3Protocol.PermissionDetail, deadline.Token);
                }, failures);
                ServerFailureObserver.ThrowIfAny(failures);
                return true;
            }, deadline.Token), deadline.Token);
    }
}
