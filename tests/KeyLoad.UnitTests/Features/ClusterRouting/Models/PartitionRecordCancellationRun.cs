using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.ClusterRouting.Helpers;

internal sealed record PartitionRecordCancellationRun(PartitionRecordPage? Page,
    Exception? ReadFailure, Exception? ObserverFailure, long ExaminedBytesBeyondBaseline,
    CancellationToken CancellationToken);
