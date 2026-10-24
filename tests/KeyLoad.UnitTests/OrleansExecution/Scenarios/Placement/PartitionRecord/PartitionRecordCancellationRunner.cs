using KeyLoad.Core.StoragePartitions.Contracts;
using KeyLoad.Core.StoragePartitions.Queries;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.OrleansExecution.Helpers;

internal static class PartitionRecordCancellationRunner
{
    internal static CancellationRun Run(ZoneTreeStore store, PartitionRef partition,
        string family, long separatelyMeasuredPageBytes, int maxRecords,
        long maxRetainedBytes, long maxExaminedBytes)
    {
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        PartitionRecordPage? page = null;
        var readerFailures = new List<Exception>();
        var observerFailures = new List<Exception>();
        long examinedBytes = 0;
        void RuntimeObserveWork(long bytes)
        {
            ServerFailureObserver.Observe(() =>
            {
                examinedBytes = checked(examinedBytes + bytes);
                if (examinedBytes > separatelyMeasuredPageBytes)
                {
                    cancellation.Cancel();
                }
            }, observerFailures);
            ServerFailureObserver.ThrowIfAny(observerFailures);
        }
        ServerFailureObserver.Observe(() => page = store.Read(view => PartitionRecordPageReader.Read(view,
            partition, family, maxRecords, maxRetainedBytes, maxExaminedBytes, RuntimeObserveWork,
            cancellationToken: token)), readerFailures);
        PartitionRecordCancellationFailures.ThrowUnexpected(readerFailures, observerFailures,
            [], [], token);
        return new(page, PartitionRecordCancellationFailures.Single(readerFailures),
            PartitionRecordCancellationFailures.Single(observerFailures), examinedBytes, token);
    }

    internal sealed record CancellationRun(PartitionRecordPage? Page, Exception? ReadFailure,
        Exception? ObserverFailure, long ExaminedBytesBeyondBaseline, CancellationToken CancellationToken);
}
