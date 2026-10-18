using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record RemoteTransferLateTargetBatchState(CreateQueueTransfer Transfer,
    QueueTransferInspection Pending, CommandRequest FailedRequest, Result<object> Failure,
    byte[] FailureBytes, byte[] OriginalCounters);
