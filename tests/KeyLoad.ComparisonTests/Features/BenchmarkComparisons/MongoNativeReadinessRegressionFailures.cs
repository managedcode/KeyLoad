using System.Runtime.ExceptionServices;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Retains every actual terminal task outcome in the closed original operation and cleanup stages.</summary>
internal static class MongoNativeReadinessRegressionFailures
{
    private const int NoFailures = 0;
    internal static Task JoinAsync(params Task[] originals)
        => Task.WhenAll(originals).ContinueWith(completed =>
        {
            _ = completed.Exception;
            var failures = new List<Exception>();
            foreach (var original in originals)
            {
                if (original.Exception is { } fault)
                {
                    failures.AddRange(fault.InnerExceptions);
                }
                else if (original.IsCanceled)
                {
                    failures.Add(ActualCancellation(original));
                }
            }
            if (failures is [var only])
            {
                ExceptionDispatchInfo.Capture(only).Throw();
            }
            if (failures.Count != NoFailures)
            {
                throw new AggregateException(failures);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private static OperationCanceledException ActualCancellation(Task original)
    {
        try
        {
            original.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException cancellation)
        {
            return cancellation;
        }
        throw new InvalidOperationException(MongoNativeReadinessRegressionProtocol.Failure);
    }
}
