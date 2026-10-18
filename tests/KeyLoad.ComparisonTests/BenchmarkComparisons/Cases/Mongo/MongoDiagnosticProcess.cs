using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class MongoDiagnosticProcess
{
    internal const int MaximumCharacters = 4096;
    private const int TimeoutSeconds = 30;
    private const string Node = "node", Eval = "-e";
    private const string Failure = "MongoDiagnosticProjectionFailed";
    private const string SourcePath = "build/KeyLoad.LoadTests.AppHost/Containers/MongoDb/IsolatedMongoInitiate.js";

    internal static async Task<string> RunAsync(string harness, string scenario)
    {
        using var process = new Process { StartInfo = StartInfo(harness, scenario) };
        if (!process.Start())
        {
            throw new InvalidOperationException(Failure);
        }
        using var deadlineTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds), TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken, deadlineTimeout.Token);
        var output = ReadAsync(process.StandardOutput, deadline.Token);
        var error = ReadAsync(process.StandardError, deadline.Token);
        Exception? primaryFailure = null;
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            var text = await output;
            if (process.ExitCode != 0 || (await error).Length != 0)
            {
                throw new InvalidOperationException(Failure);
            }
            return text;
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
            throw;
        }
        finally
        {
            await CleanupAsync(process, deadline, output, error, primaryFailure);
        }
    }

    private static ProcessStartInfo StartInfo(string harness, string scenario)
    {
        var start = new ProcessStartInfo(Node) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Eval);
        start.ArgumentList.Add(harness);
        start.ArgumentList.Add(Path.Combine(ImageBundleRealProtocol.RepositoryRoot(), SourcePath));
        start.ArgumentList.Add(scenario);
        return start;
    }

    private static async Task CleanupAsync(Process process, CancellationTokenSource deadline, Task output, Task error, Exception? primaryFailure)
    {
        var failures = new List<Exception>();
        await CollectAsync(deadline.CancelAsync(), failures);
        await CollectAsync(ReapAsync(process), failures);
        using var streams = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds), TimeProvider.System);
        try
        {
            await CollectAsync(output.WaitAsync(streams.Token), failures);
            await CollectAsync(error.WaitAsync(streams.Token), failures);
        }
        finally { Observe(output); Observe(error); }
        if (failures.Count == 0)
        {
            return;
        }
        if (primaryFailure is not null)
        {
            failures.Insert(0, primaryFailure);
        }
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        throw new AggregateException(failures);
    }

    private static async Task CollectAsync(Task task, List<Exception> failures)
    {
        await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (task.IsFaulted)
        {
            failures.AddRange(task.Exception!.InnerExceptions);
        }
        else if (task.IsCanceled)
        {
            failures.Add(new TaskCanceledException(task));
        }
    }

    private static async Task<string> ReadAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[MaximumCharacters + 1];
        var length = 0;
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(length), token);
            if (count == 0)
            {
                return new string(buffer, 0, length);
            }
            length += count;
            if (length > MaximumCharacters)
            {
                throw new InvalidOperationException(Failure);
            }
        }
    }

    private static async Task ReapAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) when (process.HasExited) { }
        catch (Win32Exception) when (process.HasExited) { }
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds), TimeProvider.System);
        await process.WaitForExitAsync(cleanup.Token);
    }

    private static void Observe(Task task)
        => _ = task.ContinueWith(static completed => _ = completed.Exception,
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
