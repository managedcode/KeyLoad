namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class SeriesPortableBackupTests
{
    [Test]
    public Task SeriesFloorAndIdReceiptsRestoreThroughCliThenBoundedContinuationSurvivesTwoColdOwners()
        => SeriesPortableBackupTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
