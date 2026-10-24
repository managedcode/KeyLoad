using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Messaging;

internal sealed record RemoteTransferCapacityAuthorityTransfer(CreateQueueTransfer Mutation,
    CommandRequest Create, Result<object> Created, QueueTransferInspection Intent);
