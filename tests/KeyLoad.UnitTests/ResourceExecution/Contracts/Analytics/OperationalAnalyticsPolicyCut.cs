using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record OperationalAnalyticsPolicyCut(string[] Rows, long Position, StoreIdentity Identity,
    (long LastIndex, long CommittedIndex, long Term) Journal);
