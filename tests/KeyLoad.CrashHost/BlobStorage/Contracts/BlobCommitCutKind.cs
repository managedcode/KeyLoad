namespace KeyLoad.CrashHost.Features.BlobStorage;

internal enum BlobCommitCutKind
{
    HeaderWritten,
    JournalFlushed,
    UnknownAfterJournalFlush,
    Acknowledged
}
