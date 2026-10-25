namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class ConfigureResourceOriginalLifetimeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task CreatedAndReconfiguredResourceOutcomeBindsActualLifetimeAndKeepsFreshPrincipalAuthority(bool table)
        => ConfigureResourceOriginalLifetimeFlow.RunAsync(table, TestContext.Current!.Execution.CancellationToken);
}
