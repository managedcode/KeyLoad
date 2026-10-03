using System.Diagnostics;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedGitHubNativeProcess
{
    public static Task<SiteProcessResult> RunAsync(ProcessStartInfo start, CancellationToken token) =>
        RunAsync(start, TimeSpan.FromMinutes(SiteIsolatedGitHubTokens.NativeDeadlineMinutes), token);

    public static Task<SiteProcessResult> RunProbeAsync(ProcessStartInfo start, CancellationToken token) =>
        RunAsync(start, TimeSpan.FromSeconds(SiteIsolatedGitHubTokens.DeadlineSeconds), token);

    private static async Task<SiteProcessResult> RunAsync(ProcessStartInfo start, TimeSpan bound, CancellationToken token)
    {
        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException(SiteIsolatedGitHubTokens.NodeFailure);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(bound);
        var stdout = SiteIsolatedGitHubProcessOutput.ReadAsync(process.StandardOutput,
            SiteIsolatedGitHubProcessOutput.MaximumOutputCharacters, deadline.Token);
        var stderr = SiteIsolatedGitHubProcessOutput.ReadAsync(process.StandardError,
            SiteIsolatedGitHubProcessOutput.MaximumErrorCharacters, deadline.Token);
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
