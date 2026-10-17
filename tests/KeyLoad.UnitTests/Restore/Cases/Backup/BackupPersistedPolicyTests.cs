namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class BackupPersistedPolicyTests
{
    [Test]
    public Task CliRestoredCredentialsAndCurrentFieldPolicySurviveTwoColdOpens()
        => BackupPersistedPolicyTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
