using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Retains admitted-work test tasks until their real completion is observed.</summary>
internal sealed class AnalyticalAdmissionTaskLifetime(TimeSpan timeout)
{
    private const string CleanupFailureMessage = "Analytical admission cleanup failed.";

    private readonly Dictionary<Task, bool> observations = [];

    internal Task<TResult> StartWorker<TResult>(Func<TResult> work)
        => StartWorker(work, expectCancellation: false);

    internal Task<TResult> StartExpectedCancellation<TResult>(Func<TResult> work)
        => StartWorker(work, expectCancellation: true);

    internal Task<bool> StartAdmissionWait(DatabaseEngine database)
        => StartWorker(() => SpinWait.SpinUntil(() => database.QueryReadsInFlight == 1, timeout));

    internal Task<KeyLoadException>[] StartRejectedRequests(params Action[] requests)
    {
        var workers = new Task<KeyLoadException>[requests.Length];
        for (var index = 0; index < requests.Length; index++)
        {
            var request = requests[index];
            workers[index] = StartWorker(() => Assert.ThrowsExactly<KeyLoadException>(request));
        }

        return workers;
    }

    internal async Task AssertRejectedAsync(Task<KeyLoadException>[] workers)
    {
        foreach (var worker in workers)
        {
            var error = await worker.WaitAsync(timeout, TimeProvider.System);
            await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        }
    }

    internal async Task AssertWorkerCompletesAsync<TResult>(Task<TResult> worker)
    {
        _ = await worker.WaitAsync(timeout, TimeProvider.System);
    }

    internal async Task AssertWorkerIsCancelledAsync(Task worker)
    {
        _ = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => worker.WaitAsync(timeout, TimeProvider.System));
    }

    internal async Task RunWithCleanupAsync(Func<Task> operation, Func<Task> cancel, Func<Task> release,
        Func<Task> observeAdmittedWorker)
    {
        var failures = new List<Exception>();
        try
        {
            await CaptureFailureAsync(operation, failures);
            await CaptureFailureAsync(cancel, failures);
            await CaptureFailureAsync(release, failures);
            await CaptureFailureAsync(observeAdmittedWorker, failures);
        }
        finally
        {
            await ObserveWorkersAsync(failures);
        }

        if (failures.Count != 0)
        {
            throw new AggregateException(CleanupFailureMessage, failures);
        }
    }

    private Task<TResult> StartWorker<TResult>(Func<TResult> work, bool expectCancellation)
    {
        var worker = Task.Run(work);
        observations.Add(worker, expectCancellation);
        return worker;
    }

    private static async Task CaptureFailureAsync(Func<Task> operation, List<Exception> failures)
    {
        try
        {
            await InvokeWithFailureWrapperAsync(operation);
        }
        catch (AggregateException error)
        {
            failures.AddRange(error.InnerExceptions);
        }
    }

    private async Task ObserveWorkersAsync(List<Exception> failures)
    {
        foreach (var (worker, expectCancellation) in observations)
        {
            await CaptureFailureAsync(() => ObserveWorkerAsync(worker, expectCancellation), failures);
        }
    }

    private static async Task InvokeWithFailureWrapperAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception error)
        {
            throw new AggregateException(CleanupFailureMessage, error);
        }
    }

    private static async Task ObserveWorkerAsync(Task worker, bool expectCancellation)
    {
        try
        {
            await worker;
        }
        catch (OperationCanceledException error) when
            (expectCancellation && error.GetType() == typeof(OperationCanceledException))
        {
            // The post-release bounded assertion verifies this exact expected outcome.
        }
    }
}
