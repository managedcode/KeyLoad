using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeTestSelectionProcess
{
    internal static async Task<string> ReadAsync(string suite)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KeyLoad.slnx")))
        {
            directory = directory.Parent;
        }
        var root = directory?.FullName ?? throw new DirectoryNotFoundException("Native selection requires the checkout.");
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "--input-type=module", "--eval", NativeTestSelectionProgram.Source,
            Path.Combine(root, "scripts", "Features", "TestInfrastructure", "run-tests.mjs"), suite })
        {
            start.ArgumentList.Add(argument);
        }
        using var process = new Process { StartInfo = start };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15), TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current!.Execution.CancellationToken);
        if (!process.Start())
        {
            throw new InvalidOperationException("Native selection Node process did not start.");
        }
        var output = process.StandardOutput.ReadToEndAsync(lifetime.Token);
        var error = process.StandardError.ReadToEndAsync(lifetime.Token);
        try
        {
            await process.WaitForExitAsync(lifetime.Token).ConfigureAwait(false);
            var results = await Task.WhenAll(output, error).ConfigureAwait(false);
            await Assert.That(process.ExitCode).IsEqualTo(0).Because(results[1]);
            await Assert.That(results[1]).IsEmpty();
            return results[0];
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3), TimeProvider.System);
            await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
            await ((Task)Task.WhenAll(output, error)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
