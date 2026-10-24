namespace KeyLoad.UnitTests.StorageRecovery;

internal sealed class DocumentIndexCommittedScanTests
{
    [Test]
    public Task AcCutDocumentIndexScanKeepsOneOldCutAcrossAtomicWriterAndReopen()
        => DocumentIndexCommittedScanScenario.RunAsync();
}
