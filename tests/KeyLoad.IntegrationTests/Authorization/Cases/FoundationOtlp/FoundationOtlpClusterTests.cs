using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed class FoundationOtlpClusterTests
{
    [Test]
    [NotInParallel]
    public async Task DashboardReceivesActualClusterSdkMcpPolicyRepairTracesAndPrivateFreeRejectionLogs()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var clock = TimeProvider.System;
        var options = FoundationOtlpClusterOptions.Create();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(FoundationOtlpClusterSelection.Dashboard);
            await ServerFailureObserver.ObserveAsync(() =>
                RunObservedAsync(fixture, options, clock, failures, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunObservedAsync(ClusterFixture fixture,
        IOptions<FoundationOtlpClusterExecutionOptions> options, TimeProvider clock,
        List<Exception> failures, CancellationToken token)
    {
        await fixture.InitializeAsync();
        using var receiver = await FoundationOtlpClusterReceiver.ConnectAsync(fixture, options, token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var (state, trace) = await FoundationOtlpClusterWorkload.RunAsync(fixture, token);
            await FoundationOtlpClusterExports.RunAsync(fixture, receiver, state, trace, options, clock, token);
        }, failures);
    }
}
