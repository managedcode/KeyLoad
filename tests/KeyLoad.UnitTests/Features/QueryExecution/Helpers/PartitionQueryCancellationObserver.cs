using System.Diagnostics;
using KeyLoad.Core;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class PartitionQueryCancellationObserver
{
    private const string ThreadName = "partition-query-cancellation-observer";
    private const string ProgressTimeoutMessage = "The partition query did not charge native read bytes before the observer bound.";
    private const string EarlyCompletionMessage = "The partition query completed before cancellation was observed on charged native reads.";
    private const string JoinTimeoutMessage = "The partition-query observer exceeded its initial join bound.";
    internal const int TimeoutSeconds = 15;

    private readonly ReadExecutionBudget budget;
    private readonly CancellationTokenSource cancellation;
    private readonly ManualResetEventSlim armed;
    private readonly ManualResetEventSlim querySettled;
    private readonly System.Threading.Lock failureGate = new();
    private readonly List<Exception> failures = [];
    private readonly Thread thread;
    private int cancellationRequested;
    private int queryStarted;
    private long observedReadBytes;
    private bool threadStarted;

    internal PartitionQueryCancellationObserver(ReadExecutionBudget budget, CancellationTokenSource cancellation,
        ManualResetEventSlim armed, ManualResetEventSlim querySettled)
    {
        this.budget = budget;
        this.cancellation = cancellation;
        this.armed = armed;
        this.querySettled = querySettled;
        thread = new Thread(Observe) { IsBackground = false, Name = ThreadName };
    }

    internal bool CancellationRequested => Volatile.Read(ref cancellationRequested) != 0;
    internal long ObservedReadBytes => Interlocked.Read(ref observedReadBytes);

    internal void StartAndWait()
    {
        thread.Start();
        threadStarted = true;
        if (!armed.Wait(TimeSpan.FromSeconds(TimeoutSeconds)))
        {
            throw new TimeoutException("The native partition-query observer did not arm within its bound.");
        }
    }

    internal void BeginQuery() => Interlocked.Exchange(ref queryStarted, 1);

    internal List<Exception> JoinAfterQuery()
    {
        var joinedFailures = new List<Exception>();
        if (!threadStarted)
        {
            RequestCancellation();
        }
        else
        {
            JoinWithinBound(joinedFailures);
            JoinOriginalThread(joinedFailures);
        }
        lock (failureGate)
        {
            joinedFailures.AddRange(failures);
        }
        return joinedFailures;
    }

    private void Observe()
    {
        try
        {
            armed.Set();
            WaitForReadProgressAndCancel();
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            RecordAndCancel(failure);
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            RecordAndCancel(failure);
        }
    }

    private void WaitForReadProgressAndCancel()
    {
        var startedAt = Stopwatch.GetTimestamp();
        while (true)
        {
            var readBytes = budget.ReadBytes;
            if (readBytes > 0)
            {
                Interlocked.Exchange(ref observedReadBytes, readBytes);
                RequestCancellation();
                return;
            }
            if (querySettled.IsSet)
            {
                if (Volatile.Read(ref queryStarted) != 0)
                {
                    Record(new InvalidOperationException(EarlyCompletionMessage));
                }
                return;
            }
            if (cancellation.IsCancellationRequested)
            {
                Record(new InvalidOperationException(ProgressTimeoutMessage));
                return;
            }
            if (Stopwatch.GetElapsedTime(startedAt) >= TimeSpan.FromSeconds(TimeoutSeconds))
            {
                RecordAndCancel(new TimeoutException(ProgressTimeoutMessage));
                return;
            }
            Thread.Yield();
        }
    }

    private void JoinWithinBound(List<Exception> joinedFailures)
    {
        try
        {
            if (!thread.Join(TimeSpan.FromSeconds(TimeoutSeconds)))
            {
                joinedFailures.Add(new TimeoutException(JoinTimeoutMessage));
                RequestCancellation();
            }
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            joinedFailures.Add(failure);
            RequestCancellation();
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            joinedFailures.Add(failure);
            RequestCancellation();
        }
    }

    private void JoinOriginalThread(List<Exception> joinedFailures)
    {
        while (thread.IsAlive)
        {
            try
            {
                thread.Join();
            }
            catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
            {
                joinedFailures.Add(failure);
            }
            catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
            {
                joinedFailures.Add(failure);
            }
        }
    }

    private void RequestCancellation()
    {
        try
        {
            cancellation.Cancel();
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            Record(failure);
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            Record(failure);
        }
        finally
        {
            if (cancellation.IsCancellationRequested)
            {
                Interlocked.Exchange(ref cancellationRequested, 1);
            }
        }
    }

    private void RecordAndCancel(Exception failure)
    {
        Record(failure);
        RequestCancellation();
    }

    private void Record(Exception failure)
    {
        lock (failureGate)
        {
            if (!failures.Contains(failure, ReferenceEqualityComparer.Instance))
            {
                failures.Add(failure);
            }
        }
    }
}
