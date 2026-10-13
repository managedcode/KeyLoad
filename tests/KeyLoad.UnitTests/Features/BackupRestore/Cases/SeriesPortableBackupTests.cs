namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class SeriesPortableBackupTests
{
    [Test]
    public Task NativeSeriesFloorAndIdReceiptsRestoreThroughCliThenBoundedContinuationSurvivesTwoColdOwners()
        => SeriesPortableBackupTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
