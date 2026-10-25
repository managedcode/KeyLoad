namespace KeyLoad.FunctionalTests.Messaging;

internal sealed class QueueAccountingRefusalWholeFlowTests
{
    [Test]
    [Arguments(QueueAccountingOperation.Ack, QueueAccountingColumn.InFlightMessages)]
    [Arguments(QueueAccountingOperation.Ack, QueueAccountingColumn.InFlightBytes)]
    [Arguments(QueueAccountingOperation.Ack, QueueAccountingColumn.StoredMessages)]
    [Arguments(QueueAccountingOperation.Ack, QueueAccountingColumn.StoredBytes)]
    [Arguments(QueueAccountingOperation.Nack, QueueAccountingColumn.InFlightBytes)]
    [Arguments(QueueAccountingOperation.Processing, QueueAccountingColumn.InFlightMessages)]
    [Arguments(QueueAccountingOperation.Reclaim, QueueAccountingColumn.InFlightBytes)]
    [Arguments(QueueAccountingOperation.ReadyExpiry, QueueAccountingColumn.StoredBytes)]
    [Arguments(QueueAccountingOperation.ScheduledExpiry, QueueAccountingColumn.StoredBytes)]
    public Task InsufficientUsageRefusesAtomicallyAcrossColdThenOriginalOperationAndHealthyWorkSucceed(
        QueueAccountingOperation operation, QueueAccountingColumn column)
        => QueueAccountingFaultTrial.RunAsync(operation, column, TestContext.Current!.Execution.CancellationToken);
}
