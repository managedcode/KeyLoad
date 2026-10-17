namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record DocumentIndexCommittedScanCut(long Position, DocumentIndexCommittedDocument[] Documents,
    Dictionary<string, string[]> IndexIds);
