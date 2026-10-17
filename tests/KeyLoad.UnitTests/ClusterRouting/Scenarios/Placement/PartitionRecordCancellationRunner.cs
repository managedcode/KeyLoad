using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterRouting.Helpers;

internal static class PartitionRecordCancellationRunner
{
    internal static PartitionRecordCancellationRun Run(ZoneTreeStore store, PartitionRef partition,
        string family, long separatelyMeasuredPageBytes, int maxRecords,
        long maxRetainedBytes, long maxExaminedBytes)
    {
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var page = new PartitionRecordPageResult();
        var readerFailures = new List<Exception>();
        var observerFailures = new List<Exception>();
        long examinedBytes = 0;
        void ObserveNativeWork(long bytes)
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
        ServerFailureObserver.Observe(() => page.Value = store.Read(view => PartitionRecordPageReader.Read(view,
            partition, family, maxRecords, maxRetainedBytes, maxExaminedBytes, ObserveNativeWork,
            cancellationToken: token)), readerFailures);
        PartitionRecordCancellationFailures.ThrowUnexpected(readerFailures, observerFailures,
            [], [], token);
        return new(page.Value, PartitionRecordCancellationFailures.Single(readerFailures),
            PartitionRecordCancellationFailures.Single(observerFailures), examinedBytes, token);
    }
}
