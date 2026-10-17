namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record NativeSagaJournalFence(string JournalName, Guid InstanceId, string NativeOwner,
    string LocalSilo, string NativeMetadataETag, long CanonicalOwnerGeneration);
