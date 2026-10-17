namespace KeyLoad.UnitTests.Features.Search;

internal sealed class Bm25PortableFamilyKeyBodyTests
{
    [Test]
    public Task ActualPublishedForeignSubjectBytesRefuseCatalogCaptureThenRepairAndSurviveBothNativeReopens()
        => Bm25PortableFamilyBackupTrial.RunKeyBodyAsync(TestContext.Current!.Execution.CancellationToken);
}
