namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedAggregateNodeGuardedInvocation
{
    private const string CaptureEnvelope = "A guarded native operation failed.";

    internal static void Capture(Action operation, Action<Exception> record)
    {
        try
        {
            Invoke(operation);
        }
        catch (AggregateException envelope)
        {
            RecordEnvelope(envelope, record);
        }
    }

    internal static async Task CaptureAsync(Func<Task> operation, Action<Exception> record)
    {
        try
        {
            await InvokeAsync(operation);
        }
        catch (AggregateException envelope)
        {
            RecordEnvelope(envelope, record);
        }
    }

    internal static async Task<T> InvokeAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception failure)
        {
            throw new AggregateException(CaptureEnvelope, failure);
        }
    }

    internal static T Invoke<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception failure)
        {
            throw new AggregateException(CaptureEnvelope, failure);
        }
    }

    internal static async Task InvokeAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception failure)
        {
            throw new AggregateException(CaptureEnvelope, failure);
        }
    }

    internal static void Invoke(Action operation)
    {
        try
        {
            operation();
        }
        catch (Exception failure)
        {
            throw new AggregateException(CaptureEnvelope, failure);
        }
    }

    internal static Exception Unwrap(AggregateException envelope)
        => envelope.InnerExceptions.Count == 1 ? envelope.InnerExceptions[0] : envelope;

    private static void RecordEnvelope(AggregateException envelope, Action<Exception> record)
    {
        foreach (var failure in envelope.InnerExceptions)
        {
            record(failure);
        }
    }
}
