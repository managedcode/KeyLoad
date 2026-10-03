using System.Diagnostics;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record ProbeRequest(string Operation, double[]? Values = null);

internal sealed record ProbeResult(JsonElement Value);

internal static class SiteNodeProbe
{
    public static async Task<ProbeResult> RunAsync(SiteTestInputs inputs, ProbeRequest request,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(SiteTokens.NodeExecutable)
        {
            WorkingDirectory = inputs.Repository,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(Path.Combine(inputs.Repository, SiteAssetTokens.ProbeRelativePath));
        start.Environment[SiteTokens.NodeRepositoryEnvironment] = inputs.Repository;
        return await SiteNodeProbeExecution.RunAsync(new Process { StartInfo = start, EnableRaisingEvents = true },
            request, cancellationToken);
    }
}

internal static class SiteNodeProbeExecution
{
    internal static async Task<ProbeResult> RunAsync(Process process, ProbeRequest request,
        CancellationToken cancellationToken)
    {
        using (process)
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(SiteTokens.NodeProbeDidNotStart);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SiteTokens.NodeTimeoutMilliseconds);
            var stdout = SiteProcessOutput.ReadAsync(process.StandardOutput, SiteTokens.NodeOutputExceeded, timeout.Token);
            var stderr = SiteProcessOutput.ReadAsync(process.StandardError, SiteTokens.NodeOutputExceeded, timeout.Token);
            try
            {
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), timeout.Token);
                process.StandardInput.Close();
                var exit = process.WaitForExitAsync(timeout.Token);
                await Task.WhenAny(exit, stdout, stderr);
                await exit;
                var output = await stdout;
                var error = await stderr;
                if (process.ExitCode != SiteTokens.ProcessSuccessExitCode || error.Length != SiteTokens.Zero)
                {
                    throw new InvalidOperationException(SiteTokens.NodeProbeFailure + " " + error);
                }
                using var document = JsonDocument.Parse(output);
                return new(document.RootElement.GetProperty(SiteTokens.Result).Clone());
            }
            catch (Exception)
            {
                await SiteNodeProbeCleanup.StopAndObserveAsync(process, stdout, stderr);
                throw;
            }
        }
    }
}

internal static class SiteNodeProbeCleanup
{
    internal static async Task StopAndObserveAsync(Process process, Task<string> stdout, Task<string> stderr)
    {
        try
        {
            await SiteProcessCleanup.StopAsync(process);
        }
        finally
        {
            await SiteProcessCleanup.ObserveCapturesAsync(process, stdout, stderr);
        }
    }
}
