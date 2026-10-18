namespace KeyLoad.RecoveryHost.Features.BlobStorage;

internal enum BlobCommitCutKind
{
    HeaderWritten,
    JournalFlushed,
    UnknownAfterJournalFlush,
    Acknowledged
}
