namespace KeyLoad.IntegrationTests.Features.Search;

[NotInParallel]
internal sealed class AnnPublicCancellationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ActualSignedReadAdmissionCancellationSettlesBeforeHealthySdkOfficialMcpAndCallReads(bool useMcp)
        => AnnPublicCancellationScenario.RunAsync(useMcp, TestContext.Current!.Execution.CancellationToken);
}
