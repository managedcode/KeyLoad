namespace KeyLoad.FunctionalTests.Messaging;

internal sealed class QueueStoredCapacityWholeFlowTests
{
    [Test]
    [Arguments(QueueStoredCapacityFault.ZeroUsageWithBodies)]
    [Arguments(QueueStoredCapacityFault.RecordsWithoutBytes)]
    [Arguments(QueueStoredCapacityFault.BytesWithoutRecords)]
    [Arguments(QueueStoredCapacityFault.UsageWithoutBodies)]
    public Task AcMsgStoredCapacity001CorruptUsageCannotAdmitProducerOrDeliveryThenColdRepairAndQuotaAtomicContinuation(
        QueueStoredCapacityFault fault)
        => QueueStoredCapacityWholeFlow.RunAsync(fault, TestContext.Current!.Execution.CancellationToken);
}
