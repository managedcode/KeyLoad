namespace KeyLoad.FunctionalTests.BackupRestore;

[NotInParallel]
internal sealed class ArchiveEntryTests
{
    [Test]
    [Arguments(ArchiveEntryProtocol.Manifest)]
    [Arguments(ArchiveEntryProtocol.Identity)]
    [Arguments(ArchiveEntryProtocol.Journal)]
    public Task RequiredClosedArchiveFifoRefusesThenExactRepairRestoresIndexedDocumentsAndOriginalOutcomes(string file)
        => ArchiveEntryFlow.RunAsync(file, TestContext.Current!.Execution.CancellationToken);
}
