namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record SagaJournalFence(string JournalName, Guid InstanceId, string RuntimeOwner,
    string LocalSilo, string RuntimeMetadataETag, long CanonicalOwnerGeneration);
