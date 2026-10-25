namespace KeyLoad.FunctionalTests.BackupRestore;

internal sealed class BackupPersistedPolicyTests
{
    [Test]
    public Task CliRestoredCredentialsAndCurrentFieldPolicySurviveTwoColdOpens()
        => BackupPersistedPolicyTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
