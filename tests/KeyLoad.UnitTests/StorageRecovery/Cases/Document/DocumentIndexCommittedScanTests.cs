namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class DocumentIndexCommittedScanTests
{
    [Test]
    public Task AcCutDocumentIndexScanKeepsOneOldCutAcrossAtomicWriterAndReopen()
        => DocumentIndexCommittedScanScenario.RunAsync();
}
