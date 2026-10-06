namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensivePinnedImageOutput : IDisposable
{
    private readonly MemoryStream buffer = new();

    internal byte[] Bytes => buffer.ToArray();

    internal async Task ReadAsync(Stream stream, CancellationToken token)
    {
        var chunk = new byte[TimeSeriesIntensivePinnedImageProtocol.BufferBytes];
        while (true)
        {
            var count = await stream.ReadAsync(chunk.AsMemory(), token);
            if (count == 0)
            {
                return;
            }

            var remaining = TimeSeriesIntensivePinnedImageProtocol.MaximumBytes - (int)buffer.Length;
            await buffer.WriteAsync(chunk.AsMemory(0, Math.Min(count, remaining)), token);
            if (count > remaining)
            {
                throw new InvalidDataException(TimeSeriesIntensivePinnedImageProtocol.OutputFailure);
            }
        }
    }

    internal static async Task<Exception?> ObserveAsync(Task task, ICollection<string> failures, Exception? primary)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(TimeSeriesIntensivePinnedImageProtocol.OperationSeconds), TimeProvider.System).ConfigureAwait(false);
            return null;
        }
        catch (TimeoutException timeout)
        {
            failures.Add(timeout.GetType().FullName ?? nameof(TimeoutException));
            _ = task.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return timeout;
        }
        catch (Exception error) when (primary is not null || TimeSeriesIntensivePinnedImageFailure.IsNative(error))
        {
            failures.Add(error.GetType().FullName ?? nameof(Exception));
            return error;
        }
    }

    public void Dispose() => buffer.Dispose();
}
