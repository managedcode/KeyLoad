namespace KeyLoad.UnitTests.BackupRestore;

internal sealed class SeriesPortableBackupTests
{
    [Test]
    public Task SeriesFloorAndIdReceiptsRestoreThroughCliThenBoundedContinuationSurvivesTwoColdOwners()
        => SeriesPortableBackupTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
