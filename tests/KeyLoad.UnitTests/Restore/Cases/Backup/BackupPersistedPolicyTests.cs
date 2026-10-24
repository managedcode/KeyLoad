namespace KeyLoad.UnitTests.BackupRestore;

internal sealed class BackupPersistedPolicyTests
{
    [Test]
    public Task CliRestoredCredentialsAndCurrentFieldPolicySurviveTwoColdOpens()
        => BackupPersistedPolicyTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
