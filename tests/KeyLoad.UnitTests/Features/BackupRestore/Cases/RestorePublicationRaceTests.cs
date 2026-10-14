namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class RestorePublicationRaceTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReconciledNativeCutRefusesOccupiedPublicationThenSameTupleRepairAndTwoColdOwners(bool existingEmpty)
        => await RestorePublicationRaceFlow.RunAsync(existingEmpty,
            TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
}
