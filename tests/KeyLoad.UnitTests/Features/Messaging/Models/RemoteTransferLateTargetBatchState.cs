namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record RemoteTransferLateTargetBatchState(CreateQueueTransfer Transfer,
    QueueTransferInspection Pending, CommandRequest FailedRequest, OperationResult Failure,
    byte[] FailureBytes, byte[] OriginalCounters);
