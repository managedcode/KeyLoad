using System.Diagnostics;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteCoverageProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal static class SiteCoverageNodeProcess
{
    public static async Task<SiteCoverageProcessResult> RunAsync(ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException(SiteCoverageTokens.NodeVersionFailure);
        }

        using var deadlineTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(SiteTokens.NodeTimeoutMilliseconds), TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineTimeout.Token);
        var standardOutput = SiteProcessOutput.ReadAsync(process.StandardOutput, SiteTokens.NodeOutputExceeded,
            deadline.Token);
        var standardError = SiteProcessOutput.ReadAsync(process.StandardError, SiteTokens.NodeOutputExceeded,
            deadline.Token);
        try
        {
            var exit = process.WaitForExitAsync(deadline.Token);
            var firstCompleted = await Task.WhenAny(exit, standardOutput, standardError).ConfigureAwait(false);
            await firstCompleted.ConfigureAwait(false);
            await exit.ConfigureAwait(false);
            return new(process.ExitCode, await standardOutput.ConfigureAwait(false),
                await standardError.ConfigureAwait(false));
        }
        catch (Exception)
        {
            try
            {
                await SiteProcessCleanup.StopAsync(process).ConfigureAwait(false);
            }
            finally
            {
                await SiteProcessCleanup.ObserveCapturesAsync(process, standardOutput, standardError)
                    .ConfigureAwait(false);
            }

            throw;
        }
    }
}
