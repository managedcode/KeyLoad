namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobPublishedRecordIntegrityTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public Task AcBlobPublishedRecordsRefuseThenExactRepairSurvivesTwoColdOwners(int fault)
        => BlobPublishedRecordIntegrityTrial.RunAsync(fault, TestContext.Current!.Execution.CancellationToken);
}
