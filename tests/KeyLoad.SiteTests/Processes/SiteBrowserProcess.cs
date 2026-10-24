using System.Diagnostics;

namespace KeyLoad.SiteTests.BenchmarkComparisons;

internal static class SiteBrowserProcess
{
    public static async Task<SiteBrowserProcessStart> StartAsync(string browserPath, string profilePath,
        SiteHeavyChildLease lease, CancellationToken cancellationToken)
    {
        try
        {
            var version = await ReadVersion(browserPath, lease, cancellationToken);
            return await SiteBrowserProcessStart.StartAsync(browserPath, profilePath, version, lease);
        }
        catch (Exception)
        {
            lease.Dispose();
            throw;
        }
    }

    private static async Task<string> ReadVersion(string browserPath, SiteHeavyChildLease lease,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(browserPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(SiteBrowserTokens.VersionArgument);
        using var process = new Process { StartInfo = start };
        if (!process.Start())
        {
            throw new InvalidOperationException(SiteBrowserTokens.BrowserVersionMissing);
        }
        using var timeoutTimeout = new CancellationTokenSource(SiteExecutionConfiguration.Options.Value.BrowserStartupTimeout, TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutTimeout.Token);
        var stdout = SiteProcessOutput.ReadAsync(process.StandardOutput, SiteBrowserTokens.BrowserResponseExceeded, timeout.Token);
        var stderr = SiteProcessOutput.ReadAsync(process.StandardError, SiteBrowserTokens.BrowserResponseExceeded, timeout.Token);
        try
        {
            lease.MarkStarted(process);
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != SiteTokens.ProcessSuccessExitCode || string.IsNullOrWhiteSpace(output) ||
                error.Length != SiteTokens.Zero || output.Length > SiteBrowserTokens.MaximumBrowserOutputCharacters)
            {
                throw new InvalidOperationException(SiteBrowserTokens.BrowserVersionMissing);
            }
            if (!lease.CompleteIfSettled(stdout, stderr))
            {
                throw new InvalidOperationException(SiteBrowserTokens.BrowserVersionMissing);
            }

            return output.Trim();
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or
            InvalidOperationException or ObjectDisposedException or TimeoutException)
        {
            await SiteHeavyChildLease.StopAndObserveAsync(process, stdout, stderr, lease);
            throw;
        }
    }
}
