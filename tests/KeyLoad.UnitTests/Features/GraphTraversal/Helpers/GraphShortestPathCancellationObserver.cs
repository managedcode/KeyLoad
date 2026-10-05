using System.Diagnostics;
using KeyLoad.Core;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphShortestPathCancellationObserver
{
    private const string ThreadName = "graph-shortest-path-cancellation-observer";
    private const string ProgressTimeoutMessage = "The shortest-path scan did not charge adjacency bytes before cancellation.";
    private const string JoinTimeoutMessage = "The shortest-path cancellation observer exceeded its join bound.";
    internal const int TimeoutSeconds = 15;

    private readonly ReadExecutionBudget budget;
    private readonly CancellationTokenSource cancellation;
    private readonly ManualResetEventSlim started;
    private readonly long minimumReadBytes;
    private readonly System.Threading.Lock failureGate = new();
    private readonly List<Exception> observerFailures = [];
    private readonly Thread thread;
    private int cancellationRequested;
    private long observedReadBytes;
    private bool threadStarted;

    internal GraphShortestPathCancellationObserver(ReadExecutionBudget budget,
        CancellationTokenSource cancellation, ManualResetEventSlim started, long minimumReadBytes)
    {
        this.budget = budget;
        this.cancellation = cancellation;
        this.started = started;
        this.minimumReadBytes = minimumReadBytes;
        thread = new Thread(Observe) { IsBackground = false, Name = ThreadName };
    }

    internal bool CancellationRequested => Volatile.Read(ref cancellationRequested) != 0;
    internal long ObservedReadBytes => Interlocked.Read(ref observedReadBytes);

    internal void StartAndWait()
    {
        thread.Start();
        threadStarted = true;
        if (!started.Wait(TimeSpan.FromSeconds(TimeoutSeconds)))
        {
            throw new TimeoutException(ProgressTimeoutMessage);
        }
    }

    internal List<Exception> JoinAfterPath()
    {
        var failures = new List<Exception>();
        if (!threadStarted)
        {
            RequestCancellation();
        }
        else
        {
            JoinWithinBound(failures);
            JoinOriginalThread(failures);
        }
        lock (failureGate)
        {
            failures.AddRange(observerFailures);
        }
        return failures;
    }

    private void Observe()
    {
        try
        {
            started.Set();
            WaitForAdjacencyAndCancel();
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

    private void WaitForAdjacencyAndCancel()
    {
        var observedAt = Stopwatch.GetTimestamp();
        while (true)
        {
            var readBytes = budget.ReadBytes;
            if (readBytes > minimumReadBytes)
            {
                Interlocked.Exchange(ref observedReadBytes, readBytes);
                RequestCancellation();
                return;
            }
            if (cancellation.IsCancellationRequested)
            {
                return;
            }
            if (TimeProvider.System.GetElapsedTime(observedAt) >= TimeSpan.FromSeconds(TimeoutSeconds))
            {
                throw new TimeoutException(ProgressTimeoutMessage);
            }
            Thread.Sleep(1);
        }
    }

    private void JoinWithinBound(List<Exception> failures)
    {
        try
        {
            if (!thread.Join(TimeSpan.FromSeconds(TimeoutSeconds)))
            {
                failures.Add(new TimeoutException(JoinTimeoutMessage));
                RequestCancellation();
            }
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            RecordJoinFailure(failure, failures);
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            RecordJoinFailure(failure, failures);
        }
    }

    private void JoinOriginalThread(List<Exception> failures)
    {
        while (thread.IsAlive)
        {
            try
            {
                thread.Join();
            }
            catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
            {
                failures.Add(failure);
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

    private void RecordJoinFailure(Exception failure, List<Exception> failures)
    {
        failures.Add(failure);
        RequestCancellation();
    }

    private void Record(Exception failure)
    {
        lock (failureGate)
        {
            if (!observerFailures.Contains(failure, ReferenceEqualityComparer.Instance))
            {
                observerFailures.Add(failure);
            }
        }
    }
}
