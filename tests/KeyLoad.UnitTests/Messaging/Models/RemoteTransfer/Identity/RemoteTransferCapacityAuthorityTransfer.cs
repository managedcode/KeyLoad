namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record RemoteTransferCapacityAuthorityTransfer(CreateQueueTransfer Mutation,
    CommandRequest Create, OperationResult Created, QueueTransferInspection Intent);
