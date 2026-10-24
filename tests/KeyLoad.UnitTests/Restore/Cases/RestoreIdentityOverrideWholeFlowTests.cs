namespace KeyLoad.UnitTests.BackupRestore;

internal sealed class RestoreIdentityOverrideWholeFlowTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ExplicitOldIdentityRefusesPublicationThenFreshRestoreReplayAndTwoColdRemainExact(bool emptyDestination)
        => RestoreIdentityOverrideFlow.RunAsync(emptyDestination, TestContext.Current!.Execution.CancellationToken);
}
