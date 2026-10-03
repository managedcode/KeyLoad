using System.Diagnostics;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedGitHubNativeProcess
{
    public static async Task<SiteProcessResult> RunAsync(ProcessStartInfo start, CancellationToken token)
    {
        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException(SiteIsolatedGitHubTokens.NodeFailure);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(SiteIsolatedGitHubTokens.NativeDeadlineMinutes));
        var stdout = SiteProcessOutput.ReadAsync(process.StandardOutput, SiteIsolatedGitHubTokens.NodeFailure, deadline.Token);
        var stderr = SiteProcessOutput.ReadAsync(process.StandardError, SiteIsolatedGitHubTokens.NodeFailure, deadline.Token);
        try
        {
            var exit = process.WaitForExitAsync(deadline.Token);
            var first = await Task.WhenAny(exit, stdout, stderr);
            await first;
            await exit;
            return new(process.ExitCode, await stdout, await stderr);
        }
        catch (Exception)
        {
            try
            {
                await SiteProcessCleanup.StopAsync(process);
            }
            finally
            {
                await SiteProcessCleanup.ObserveCapturesAsync(process, stdout, stderr);
            }

            throw;
        }
    }
}
