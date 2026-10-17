namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ConfigureResourceOriginalLifetimeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task NativeCreatedAndReconfiguredResourceOutcomeBindsActualLifetimeAndKeepsFreshPrincipalAuthority(bool table)
        => ConfigureResourceOriginalLifetimeFlow.RunAsync(table, TestContext.Current!.Execution.CancellationToken);
}
