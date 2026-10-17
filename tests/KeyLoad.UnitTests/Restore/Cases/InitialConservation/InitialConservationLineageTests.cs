namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class InitialConservationLineageTests
{
    [Test]
    public async Task ActualInitialRegisteredArchiveRetainsNestedOriginsAcrossTwoFreshSlotsAndColdReplay()
        => await InitialConservationLineageFlow.RunAsync(TestContext.Current!.Execution.CancellationToken)
            .ConfigureAwait(false);
}
