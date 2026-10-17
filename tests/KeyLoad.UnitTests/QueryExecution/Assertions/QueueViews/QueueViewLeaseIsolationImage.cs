using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed record QueueViewLeaseIsolationImage(StoreIdentity Identity, long Position, string[] Rows,
    long LastIndex, long CommittedIndex, long Term);
