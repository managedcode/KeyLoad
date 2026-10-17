namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record DocumentIndexCommittedDocument(string Id, long Revision, string Json, bool Deleted);
