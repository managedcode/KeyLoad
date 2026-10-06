using KeyLoad.Core;
using KeyLoad.UnitTests.Features.TestInfrastructure;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnSeedCancellationThread
{
    private const string ObserverName = "ann-seed-cancellation-observer";
    private const string ObservationTimeoutMessage = "The seed capture did not charge a read before cancellation.";
    private const string JoinTimeoutMessage = "The ANN seed cancellation observer exceeded its join bound.";
    internal const int TimeoutSeconds = 15;

    private readonly TimeProvider clock;
    private readonly ReadExecutionBudget budget;
    private readonly CancellationTokenSource cancellation;
    private readonly ManualResetEventSlim started;
    private readonly AnnSeedCancellationFailureBag observerFailures = new();
    private readonly Thread thread;
    private int cancellationRequested;
    private long observedReadBytes;
    private bool threadStarted;

    internal AnnSeedCancellationThread(ReadExecutionBudget budget, CancellationTokenSource cancellation,
        ManualResetEventSlim started, TimeProvider? timeProvider = null)
    {
        clock = timeProvider ?? TimeProvider.System;
        this.budget = budget;
        this.cancellation = cancellation;
        this.started = started;
        thread = new Thread(Observe) { IsBackground = false, Name = ObserverName };
    }

    internal bool CancellationRequested => Volatile.Read(ref cancellationRequested) != 0;
    internal long ObservedReadBytes => Interlocked.Read(ref observedReadBytes);

    internal void StartAndWait()
    {
        thread.Start();
        threadStarted = true;
        if (!started.Wait(TimeSpan.FromSeconds(TimeoutSeconds)))
        {
            throw new TimeoutException(ObservationTimeoutMessage);
        }
    }

    internal List<Exception> JoinAfterCapture()
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
        observerFailures.CopyTo(failures);
        return failures;
    }

    private void Observe()
    {
        try
        {
            started.Set();
            WaitForChargeAndCancel();
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

    private void WaitForChargeAndCancel()
    {
        var observedAt = new TestElapsedClock(clock);
        while (true)
        {
            var readBytes = budget.ReadBytes;
            if (readBytes > 0)
            {
                Interlocked.Exchange(ref observedReadBytes, readBytes);
                RequestCancellation();
                return;
            }
            if (cancellation.IsCancellationRequested)
            {
                return;
            }
            if (observedAt.Elapsed >= TimeSpan.FromSeconds(TimeoutSeconds))
            {
                throw new TimeoutException(ObservationTimeoutMessage);
            }
            Task.Delay(TimeSpan.FromMilliseconds(1), clock).ConfigureAwait(false).GetAwaiter().GetResult();
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
            RecordCancellationFailure(failure);
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            RecordCancellationFailure(failure);
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
        observerFailures.Add(failure);
        RequestCancellation();
    }

    private void RecordJoinFailure(Exception failure, List<Exception> failures)
    {
        failures.Add(failure);
        RequestCancellation();
    }

    private void RecordCancellationFailure(Exception failure) => observerFailures.Add(failure);

}
