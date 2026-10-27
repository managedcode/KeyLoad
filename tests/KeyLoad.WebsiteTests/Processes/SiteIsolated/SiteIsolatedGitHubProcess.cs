using System.Diagnostics;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal static class SiteIsolatedGitHubProcess
{
    public static Task<SiteProcessResult> RunAsync(ProcessStartInfo start, CancellationToken token) =>
        RunAsync(start, SiteExecutionConfiguration.Options.Value.GitHubDeadline, admission: null, token);

    public static Task<SiteProcessResult> RunProbeAsync(ProcessStartInfo start, CancellationToken token,
        SiteHeavyChildAdmission? admission = null) =>
        RunAsync(start, bound: null, admission, token);

    private static async Task<SiteProcessResult> RunAsync(ProcessStartInfo start, TimeSpan? bound,
        SiteHeavyChildAdmission? admission, CancellationToken token)
    {
        using var lease = admission is null ? null : await admission.AcquireAsync(token);
        token.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException(SiteIsolatedGitHubTokens.NodeFailure);
        }
        lease?.MarkStarted(process);

        // Probes run until they exit or the caller cancels; only remote GitHub captures keep their transport bound.
        using var deadlineTimeout = bound is null ? null : new CancellationTokenSource(bound.Value, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, deadlineTimeout?.Token ?? CancellationToken.None);
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
            var result = new SiteProcessResult(process.ExitCode, await stdout, await stderr);
            _ = lease?.CompleteIfSettled(stdout, stderr);
            return result;
        }
        catch (Exception)
        {
            await SiteHeavyChildLease.StopAndObserveAsync(process, stdout, stderr, lease);
            throw;
        }
    }
}
