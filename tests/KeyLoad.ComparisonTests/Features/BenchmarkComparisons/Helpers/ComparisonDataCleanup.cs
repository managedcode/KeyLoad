using System.Diagnostics;
using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class ComparisonDataCleanup
{
    internal static async Task DeleteDataAsync(string root, ContainerResource redis)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        try
        {
            Directory.Delete(root, true);
            return;
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsLinux())
        {
        }

        var external = Path.Combine(root, "external");
        if (!Directory.Exists(external) || !redis.TryGetContainerImageName(out var image))
        {
            throw new IOException("Cannot clean the comparison run's container-owned data.");
        }

        var start = new ProcessStartInfo("docker") { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in new[]
        {
            "run", "--rm", "--pull", "never", "--network", "none", "--read-only", "--user", "0:0",
            "--cap-drop", "ALL", "--cap-add", "DAC_OVERRIDE", "--entrypoint", "/bin/sh",
            "--mount", $"type=bind,source={external},target=/data", image!, "-c", "rm -rf /data/*"
        })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new IOException("Cannot start comparison data cleanup.");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(NativeExecutionPolicyFixture.Harness().Value.ComparisonDataCleanupTimeout, TimeProvider.System);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        await output;
        if (process.ExitCode != 0)
        {
            throw new IOException($"Comparison data cleanup failed: {await error}");
        }

        await error;
        Directory.Delete(root, true);
    }
}
