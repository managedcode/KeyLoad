using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class RequestCqrsPhaseFaultTests
{
    [Test]
    public Task SdkBeforeSubmitCancellationHasNoEffectUntilStableRetry()
        => RequestCqrsPhaseFaultScenario.RunAsync(useMcp: false, phase: RequestCqrsProbePhase.BeforeSubmit,
            cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task OfficialMcpBeforeSubmitCancellationHasNoEffectUntilStableRetry()
        => RequestCqrsPhaseFaultScenario.RunAsync(useMcp: true, phase: RequestCqrsProbePhase.BeforeSubmit,
            cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task SdkSubmitReturnedCancellationReusesCommittedReceipt()
        => RequestCqrsPhaseFaultScenario.RunAsync(useMcp: false, phase: RequestCqrsProbePhase.SubmitReturned,
            cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task OfficialMcpSubmitReturnedCancellationReusesCommittedReceipt()
        => RequestCqrsPhaseFaultScenario.RunAsync(useMcp: true, phase: RequestCqrsProbePhase.SubmitReturned,
            cancellationToken: TestContext.Current!.Execution.CancellationToken);
}
