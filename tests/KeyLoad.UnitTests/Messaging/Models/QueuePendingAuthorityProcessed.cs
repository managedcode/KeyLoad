namespace KeyLoad.UnitTests.Messaging;

internal sealed record QueuePendingAuthorityProcessed(string Id, ProcessingRequest Request, CommitReceipt Receipt);
