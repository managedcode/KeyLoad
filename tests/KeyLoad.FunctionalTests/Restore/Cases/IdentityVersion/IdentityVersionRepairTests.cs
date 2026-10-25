namespace KeyLoad.FunctionalTests.BackupRestore;

internal sealed class IdentityVersionRepairTests
{
    [Test, Arguments(false), Arguments(true)]
    public Task HashConsistentUnsupportedIdentityRefusesCliThenExactRepairPreservesTwoColdCuts(bool codecVersion)
        => IdentityVersionRepairTrial.RunAsync(codecVersion, TestContext.Current!.Execution.CancellationToken);
}
