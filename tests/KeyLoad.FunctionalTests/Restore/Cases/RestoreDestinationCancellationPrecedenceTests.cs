namespace KeyLoad.FunctionalTests.BackupRestore;

internal sealed class RestoreDestinationCancellationPrecedenceTests
{
    [Test]
    public Task NonemptyTargetRefusesBeforeCanceledTokenThenSameArchiveRepairsThroughCliAndTwoColdOwners()
        => RestoreDestinationCancellationPrecedenceTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
