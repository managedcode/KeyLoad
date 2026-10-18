namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal enum BlobCommitCutKind
{
    HeaderWritten,
    JournalFlushed,
    UnknownAfterJournalFlush,
    Acknowledged
}
