namespace KeyLoad.FunctionalTests.Authorization;

internal sealed class CatalogIdentityWholeFlowTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task CrossKeyPrincipalOrCredentialRefusesThenExactRepairFreshPolicyReplayAndTwoColdRemainHealthy(bool credential)
        => CatalogIdentityWholeFlow.RunAsync(credential, TestContext.Current!.Execution.CancellationToken);
}
