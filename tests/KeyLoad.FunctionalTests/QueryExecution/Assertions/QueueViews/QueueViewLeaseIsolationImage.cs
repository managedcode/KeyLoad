using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed record QueueViewLeaseIsolationImage(StoreIdentity Identity, long Position, string[] Rows,
    long AppliedPosition, long StorePosition);
