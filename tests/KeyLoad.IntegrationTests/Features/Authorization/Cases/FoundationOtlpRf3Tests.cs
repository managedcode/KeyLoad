using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed class FoundationOtlpRf3Tests
{
    [Test]
    [NotInParallel]
    public async Task NativeDashboardReceivesActualRf3SdkMcpPolicyRepairTracesAndPrivateFreeRejectionLogs()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var clock = TimeProvider.System;
        var options = FoundationOtlpRf3Options.Create();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(FoundationOtlpRf3Selection.Dashboard);
            await ServerFailureObserver.ObserveAsync(() =>
                RunObservedAsync(fixture, options, clock, failures, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunObservedAsync(ClusterFixture fixture,
        IOptions<FoundationOtlpRf3ExecutionOptions> options, TimeProvider clock,
        List<Exception> failures, CancellationToken token)
    {
        await fixture.InitializeAsync();
        using var receiver = await FoundationOtlpRf3Receiver.ConnectAsync(fixture, options, token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var (state, trace) = await FoundationOtlpRf3Workload.RunAsync(fixture, token);
            await FoundationOtlpRf3Exports.RunAsync(fixture, receiver, state, trace, options, clock, token);
        }, failures);
    }
}
