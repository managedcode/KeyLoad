namespace KeyLoad.UnitTests.Features.BackupRestore;

[NotInParallel]
internal sealed class NativeArchiveEntryTests
{
    [Test]
    [Arguments(NativeArchiveEntryProtocol.Manifest)]
    [Arguments(NativeArchiveEntryProtocol.Identity)]
    [Arguments(NativeArchiveEntryProtocol.Journal)]
    public Task RequiredClosedArchiveFifoRefusesThenExactRepairRestoresIndexedDocumentsAndOriginalOutcomes(string file)
        => NativeArchiveEntryFlow.RunAsync(file, TestContext.Current!.Execution.CancellationToken);
}
