using System.Diagnostics;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterRouting.Helpers;

internal static class PartitionRecordCancellationRunner
{
    private const string ReaderThreadName = "partition-record-reader";
    private const string ObserverThreadName = "partition-record-read-observer";
    private const string ObserverTimeoutFailure = "Native traversal did not pass the measured read-work baseline.";
    private const int ObserverTimeoutMilliseconds = 15_000;

    internal static PartitionRecordCancellationRun Run(ZoneTreeStore store, PartitionRef partition,
        string family, long baselineExaminedBytes, long separatelyMeasuredPageBytes, int maxRecords,
        long maxRetainedBytes, long maxExaminedBytes)
    {
        using var cancellation = new CancellationTokenSource();
        var cancellationToken = cancellation.Token;
        using var observerReady = new ManualResetEventSlim();
        using var observerFinished = new ManualResetEventSlim();
        var page = new PartitionRecordPageResult();
        var readerFailures = new List<Exception>();
        var observerFailures = new List<Exception>();
        var operationFailures = new List<Exception>();
        var cleanupFailures = new List<Exception>();
        long examinedBytesBeyondBaseline = 0;
        var reader = new Thread(() => ReadPage(store, partition, family, maxRecords,
            maxRetainedBytes, maxExaminedBytes, page, readerFailures, cancellation.Token))
        { IsBackground = false, Name = ReaderThreadName };
        var observer = new Thread(() => ObserveAndCancel(store, baselineExaminedBytes,
            separatelyMeasuredPageBytes, cancellation, observerReady, observerFinished, value =>
                Interlocked.Exchange(ref examinedBytesBeyondBaseline, value), observerFailures))
        { IsBackground = false, Name = ObserverThreadName };
        var readerStarted = false;
        var observerStarted = false;

        try
        {
            StartAndObserve(observer, reader, observerReady, observerFinished,
                operationFailures, ref observerStarted, ref readerStarted);
        }
        finally
        {
            Settle(cancellation, observerReady, observerFinished, observer, reader,
                ref observerStarted, ref readerStarted, cleanupFailures);
        }

        PartitionRecordCancellationFailures.ThrowUnexpected(readerFailures, observerFailures,
            operationFailures, cleanupFailures, cancellationToken);
        return new(page.Value, PartitionRecordCancellationFailures.Single(readerFailures),
            PartitionRecordCancellationFailures.Single(observerFailures),
            Interlocked.Read(ref examinedBytesBeyondBaseline), cancellationToken);
    }

    private static void ReadPage(ZoneTreeStore store, PartitionRef partition, string family,
        int maxRecords, long maxRetainedBytes, long maxExaminedBytes,
        PartitionRecordPageResult result, List<Exception> failures, CancellationToken cancellationToken)
        => Observe(() => result.Value = store.Read(view => PartitionRecordPageReader.Read(view,
            partition, family, maxRecords, maxRetainedBytes, maxExaminedBytes,
            cancellationToken: cancellationToken)), failures);

    private static void ObserveAndCancel(ZoneTreeStore store, long baselineExaminedBytes,
        long separatelyMeasuredPageBytes, CancellationTokenSource cancellation,
        ManualResetEventSlim ready, ManualResetEventSlim finished, Action<long> observed,
        List<Exception> failures)
    {
        try
        {
            Observe(() => ObserveNativeWork(store, baselineExaminedBytes,
                separatelyMeasuredPageBytes, cancellation, ready, observed, failures), failures);
        }
        finally
        {
            Observe(finished.Set, failures);
        }
    }

    private static void ObserveNativeWork(ZoneTreeStore store, long baselineExaminedBytes,
        long separatelyMeasuredPageBytes, CancellationTokenSource cancellation,
        ManualResetEventSlim ready, Action<long> observed, List<Exception> failures)
    {
        Observe(ready.Set, failures);
        if (failures.Count != 0)
        {
            return;
        }
        var started = Stopwatch.GetTimestamp();
        while (!cancellation.IsCancellationRequested
            && Stopwatch.GetElapsedTime(started).TotalMilliseconds < ObserverTimeoutMilliseconds)
        {
            var snapshot = store.GetReadDiagnostics();
            var examinedSinceBaseline = snapshot.RangeExaminedBytes - baselineExaminedBytes;
            if (examinedSinceBaseline > separatelyMeasuredPageBytes)
            {
                observed(examinedSinceBaseline);
                Observe(cancellation.Cancel, failures);
                return;
            }
            Thread.Yield();
        }

        if (!cancellation.IsCancellationRequested)
        {
            failures.Add(new TimeoutException(ObserverTimeoutFailure));
        }
    }

    private static void Start(Thread thread, List<Exception> failures)
        => Observe(thread.Start, failures);

    private static void StartAndObserve(Thread observer, Thread reader,
        ManualResetEventSlim ready, ManualResetEventSlim finished, List<Exception> failures,
        ref bool observerStarted, ref bool readerStarted)
    {
        Start(observer, failures);
        observerStarted = IsStarted(observer);
        if (!observerStarted)
        {
            return;
        }
        WaitForReady(ready, failures);
        if (failures.Count != 0)
        {
            return;
        }
        Start(reader, failures);
        readerStarted = IsStarted(reader);
        if (readerStarted && failures.Count == 0)
        {
            WaitForCompletion(finished, failures);
        }
    }

    private static void Settle(CancellationTokenSource cancellation, ManualResetEventSlim ready,
        ManualResetEventSlim finished, Thread observer, Thread reader, ref bool observerStarted,
        ref bool readerStarted, List<Exception> failures)
    {
        Observe(cancellation.Cancel, failures);
        Observe(ready.Set, failures);
        Observe(finished.Set, failures);
        readerStarted |= IsStarted(reader);
        observerStarted |= IsStarted(observer);
        if (observerStarted)
        {
            JoinOriginal(observer, failures);
        }
        if (readerStarted)
        {
            JoinOriginal(reader, failures);
        }
        Observe(ready.Dispose, failures);
        Observe(finished.Dispose, failures);
        Observe(cancellation.Dispose, failures);
    }

    private static void WaitForReady(ManualResetEventSlim ready, List<Exception> failures)
        => Observe(() =>
        {
            if (!ready.Wait(TimeSpan.FromMilliseconds(ObserverTimeoutMilliseconds)))
            {
                throw new TimeoutException(ObserverTimeoutFailure);
            }
        }, failures);

    private static bool IsStarted(Thread thread) => thread.ThreadState != System.Threading.ThreadState.Unstarted;

    private static void WaitForCompletion(ManualResetEventSlim finished, List<Exception> failures)
        => Observe(() =>
        {
            if (!finished.Wait(TimeSpan.FromMilliseconds(ObserverTimeoutMilliseconds)))
            {
                throw new TimeoutException(ObserverTimeoutFailure);
            }
        }, failures);

    private static void JoinOriginal(Thread thread, List<Exception> failures)
    {
        do
        {
            Observe(thread.Join, failures);
            if (thread.IsAlive)
            {
                Thread.Yield();
            }
        }
        while (thread.IsAlive);
    }

    private static void Observe(Action operation, List<Exception> failures)
        => ServerFailureObserver.Observe(operation, failures);

}
