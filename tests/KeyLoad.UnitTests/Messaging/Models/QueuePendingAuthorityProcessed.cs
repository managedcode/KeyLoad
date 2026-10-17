namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueuePendingAuthorityProcessed(string Id, ProcessingRequest Request, CommitReceipt Receipt);
