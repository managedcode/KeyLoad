using System.Diagnostics;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Owns the original Docker process and both original bounded readers until their actual completion.</summary>
internal sealed class MongoNativeReadinessRegressionProcess : IDisposable
{
    private const int Undisposed = 0, Disposed = 1, StandardOutputIndex = 0;
    private readonly Process process;
    private Task? startup;
    private Task<IsolatedKeyLoadFaultRegressionOutput>? output;
    private Task<IsolatedKeyLoadFaultRegressionOutput>? error;
    private bool started;
    private bool stopAttempted;
    private int disposed;
    private int retained;

    internal MongoNativeReadinessRegressionProcess(IEnumerable<string> arguments, IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(MongoNativeReadinessRegressionProtocol.Docker)
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        if (environment is not null)
        {
            foreach (var variable in environment)
            {
                start.Environment[variable.Key] = variable.Value;
            }
        }
        process = new Process { StartInfo = start };
    }

    private void StartAndRegister(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        started = process.Start();
        MongoNativeReadinessRegressionProtocol.Require(started);
        output = IsolatedKeyLoadFaultRegressionOutput.ReadAsync(process.StandardOutput, MongoNativeReadinessRegressionProtocol.OutputCharacters);
        error = IsolatedKeyLoadFaultRegressionOutput.ReadAsync(process.StandardError, MongoNativeReadinessRegressionProtocol.ErrorCharacters);
        token.ThrowIfCancellationRequested();
    }

    internal async Task<string> CompleteAsync(CancellationToken token)
    {
        MongoNativeReadinessRegressionProtocol.Require(startup is null);
        startup = Task.Run(() => StartAndRegister(token), CancellationToken.None);
        await startup;
        var originalOutput = output ?? throw new InvalidOperationException(MongoNativeReadinessRegressionProtocol.Failure);
        var originalError = error ?? throw new InvalidOperationException(MongoNativeReadinessRegressionProtocol.Failure);
        await process.WaitForExitAsync(token);
        var originalReaders = Task.WhenAll(originalOutput, originalError);
        Observe(originalReaders);
        var readers = await originalReaders.WaitAsync(token);
        token.ThrowIfCancellationRequested();
        MongoNativeReadinessRegressionProtocol.Require(process.ExitCode == MongoNativeReadinessRegressionProtocol.SuccessfulExit
            && readers.All(reader => !reader.Oversized));
        return readers[StandardOutputIndex].Text.Trim();
    }

    internal async Task StopClientAsync(CancellationToken token)
    {
        stopAttempted = true;
        if (started)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync(token);
        }
    }

    internal async Task DrainAsync(CancellationToken token)
    {
        var originalReaders = JoinAsync(RegisteredReaders());
        Observe(originalReaders);
        await originalReaders.WaitAsync(token);
    }

    public void Dispose()
    {
        if (Volatile.Read(ref disposed) != Undisposed || Interlocked.Exchange(ref retained, Disposed) != Undisposed)
        {
            return;
        }
        Observe(ReleaseAfterSettlementAsync());
    }

    internal async Task ReleaseAsync(CancellationToken token)
    {
        var originals = RegisteredReaders().ToList();
        if (started)
        {
            originals.Add(process.WaitForExitAsync(CancellationToken.None));
        }
        var settlement = Task.WhenAll(originals);
        Observe(settlement);
        try
        {
            await settlement.WaitAsync(token);
        }
        finally
        {
            if (settlement.IsCompleted)
            {
                ReleaseProcess();
            }
        }
    }

    private async Task ReleaseAfterSettlementAsync()
    {
        try
        {
            if (startup is not null)
            {
                await startup;
            }
        }
        finally
        {
            await ReleaseRegisteredAsync();
        }
    }

    private async Task ReleaseRegisteredAsync()
    {
        var originals = RegisteredReaders().ToList();
        if (started)
        {
            if (!stopAttempted)
            {
                originals.Add(StopClientAsync(CancellationToken.None));
            }
            originals.Add(process.WaitForExitAsync(CancellationToken.None));
        }
        var settlement = Task.WhenAll(originals);
        try
        {
            await settlement;
        }
        finally
        {
            _ = settlement.Exception;
            ReleaseProcess();
        }
    }

    private void ReleaseProcess()
    {
        if (Interlocked.Exchange(ref disposed, Disposed) == Undisposed)
        {
            process.Dispose();
        }
    }

    private Task[] RegisteredReaders()
    {
        var originals = new List<Task>();
        if (output is not null)
        {
            originals.Add(output);
        }
        if (error is not null)
        {
            originals.Add(error);
        }
        return originals.ToArray();
    }

    internal static Task JoinAsync(params Task[] originals)
        => MongoNativeReadinessRegressionFailures.JoinAsync(originals);

    private static void Observe(Task task)
        => _ = task.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
