namespace KeyLoad.UnitTests.Messaging;

internal sealed class RemoteTransferCapacityAuthorityTests
{
    [Test]
    [Arguments(true, RemoteTransferCapacityAuthorityFault.Missing)]
    [Arguments(false, RemoteTransferCapacityAuthorityFault.Missing)]
    [Arguments(true, RemoteTransferCapacityAuthorityFault.Empty)]
    [Arguments(false, RemoteTransferCapacityAuthorityFault.Empty)]
    [Arguments(true, RemoteTransferCapacityAuthorityFault.UndersizedBytes)]
    [Arguments(false, RemoteTransferCapacityAuthorityFault.UndersizedBytes)]
    [Arguments(true, RemoteTransferCapacityAuthorityFault.UndersizedCount)]
    [Arguments(false, RemoteTransferCapacityAuthorityFault.UndersizedCount)]
    [Arguments(true, RemoteTransferCapacityAuthorityFault.MissingRecords)]
    [Arguments(false, RemoteTransferCapacityAuthorityFault.MissingRecords)]
    public async Task GenuineRetainedTransferRefusesInconsistentCapacityThenOriginalColdRepairAndHealthyFlow(
        bool source, RemoteTransferCapacityAuthorityFault fault)
        => await RemoteTransferCapacityAuthorityWholeFlow.RunAsync(source, fault, TestContext.Current!.Execution.CancellationToken);
}
