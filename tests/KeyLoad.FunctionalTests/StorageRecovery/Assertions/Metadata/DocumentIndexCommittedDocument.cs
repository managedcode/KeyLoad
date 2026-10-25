namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed record DocumentIndexCommittedDocument(string Id, long Revision, string Json, bool Deleted);
