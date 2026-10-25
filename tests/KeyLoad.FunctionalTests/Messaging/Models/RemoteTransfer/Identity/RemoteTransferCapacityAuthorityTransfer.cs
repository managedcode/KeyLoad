using ManagedCode.Communication;
namespace KeyLoad.FunctionalTests.Messaging;

internal sealed record RemoteTransferCapacityAuthorityTransfer(CreateQueueTransfer Mutation,
    CommandRequest Create, Result<object> Created, QueueTransferInspection Intent);
