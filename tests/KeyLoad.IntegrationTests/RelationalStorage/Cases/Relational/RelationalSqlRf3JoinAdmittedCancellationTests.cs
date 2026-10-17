namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

[NotInParallel]
internal sealed class RelationalSqlRf3JoinAdmittedCancellationTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public Task SdkAndOfficialMcpQ2CancellationAfterActualAdmissionSettlesAndContinuesAtTheOriginalCut(
        bool officialMcp, bool ast)
        => RelationalSqlRf3JoinAdmissionFlow.RunAsync(officialMcp, ast,
            TestContext.Current!.Execution.CancellationToken);
}
