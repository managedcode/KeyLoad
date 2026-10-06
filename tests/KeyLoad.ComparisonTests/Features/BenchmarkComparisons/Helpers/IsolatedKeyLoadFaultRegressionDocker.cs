using System.Diagnostics;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Executes only owned native Docker operations with bounded process lifetime, output and cancellation drain.</summary>
internal static class IsolatedKeyLoadFaultRegressionDocker
{
    private const string Docker = "docker";

    internal static async Task<string> RunAsync(string[] arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var deadline = IsolatedKeyLoadFaultRegressionProtocol.Deadline(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultCliTimeout, token);
        var start = new ProcessStartInfo(Docker)
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException(IsolatedKeyLoadFaultRegressionProtocol.Failure);
        var output = IsolatedKeyLoadFaultRegressionOutput.ReadAsync(process.StandardOutput, NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultOutputCharacters);
        var error = IsolatedKeyLoadFaultRegressionOutput.ReadAsync(process.StandardError, NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultErrorCharacters);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            var readers = await Task.WhenAll(output, error).WaitAsync(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultProcessDrainTimeout, TimeProvider.System, deadline.Token);
            IsolatedKeyLoadFaultRegressionProtocol.Require(process.ExitCode == 0 && readers.All(item => !item.Oversized));
            return readers[0].Text.Trim();
        }
        finally
        {
            await StopAndDrainAsync(process, output, error);
        }
    }

    private static async Task StopAndDrainAsync(Process process, Task<IsolatedKeyLoadFaultRegressionOutput> output,
        Task<IsolatedKeyLoadFaultRegressionOutput> error)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            using var cleanup = new CancellationTokenSource(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultProcessDrainTimeout, TimeProvider.System);
            await process.WaitForExitAsync(cleanup.Token);
            await Task.WhenAll(output, error).WaitAsync(cleanup.Token);
        }
        finally
        {
            Observe(output);
            Observe(error);
        }
    }

    private static void Observe(Task pending)
        => _ = pending.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
