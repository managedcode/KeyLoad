namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class BlobPortableBackupTests
{
    [Test]
    public Task NativeMultipartAndActiveUploadRestoreThroughCliThenFreshPublicationSurvivesTwoColdOwners()
        => BlobPortableBackupTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
