using System.ComponentModel;
using System.Diagnostics;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteBrowserProcess
{
    public static async Task<SiteBrowserProcessStart> StartAsync(string browserPath, string profilePath,
        CancellationToken cancellationToken)
    {
        var version = await ReadVersion(browserPath, cancellationToken);
        return SiteBrowserProcessStart.Start(browserPath, profilePath, version);
    }

    private static async Task<string> ReadVersion(string browserPath, CancellationToken cancellationToken)
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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SiteBrowserTokens.BrowserStartupTimeoutMilliseconds);
        var stdout = SiteProcessOutput.ReadAsync(process.StandardOutput, SiteBrowserTokens.BrowserResponseExceeded, timeout.Token);
        var stderr = SiteProcessOutput.ReadAsync(process.StandardError, SiteBrowserTokens.BrowserResponseExceeded, timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != SiteTokens.ProcessSuccessExitCode || string.IsNullOrWhiteSpace(output) ||
                error.Length != SiteTokens.Zero || output.Length > SiteBrowserTokens.MaximumBrowserOutputCharacters)
            {
                throw new InvalidOperationException(SiteBrowserTokens.BrowserVersionMissing);
            }
            return output.Trim();
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or
            InvalidOperationException or ObjectDisposedException or TimeoutException)
        {
            try
            { await SiteProcessCleanup.StopAsync(process); }
            catch (Exception cleanupException) when (cleanupException is InvalidOperationException or Win32Exception or TimeoutException)
            { /* Preserve the version or capture failure. */ }
            try
            { await SiteProcessCleanup.ObserveCapturesAsync(process, stdout, stderr); }
            catch (Exception cleanupException) when (cleanupException is IOException or ObjectDisposedException or
                OperationCanceledException or InvalidOperationException or TimeoutException)
            { /* Preserve the version or capture failure. */ }
            throw;
        }
    }
}

internal sealed class SiteBrowserProcessStart : IAsyncDisposable
{
    public Process Process { get; }
    private int transferred;
    private int disposed;

    private SiteBrowserProcessStart(Process process, string version)
    {
        Process = process;
        Version = version;
    }

    public string Version { get; }

    public static SiteBrowserProcessStart Start(string browserPath, string profilePath, string version)
    {
        var start = new ProcessStartInfo(browserPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(SiteBrowserTokens.ChromeHeadlessArgument);
        start.ArgumentList.Add(SiteBrowserTokens.RemoteDebuggingArgument);
        start.ArgumentList.Add(SiteBrowserTokens.UserDataArgumentPrefix + profilePath);
        start.ArgumentList.Add(SiteBrowserTokens.NoFirstRunArgument);
        start.ArgumentList.Add(SiteBrowserTokens.NoDefaultBrowserArgument);
        start.ArgumentList.Add(SiteBrowserTokens.BlankUrl);
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(SiteBrowserTokens.BrowserStartFailure);
            }

            return new(process, version);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or IOException)
        {
            process.Dispose();
            throw;
        }
    }

    public SiteBrowserProcessStart Transfer()
    {
        Interlocked.Exchange(ref transferred, SiteBrowserTokens.One);
        return this;
    }

    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref transferred) == SiteBrowserTokens.One)
        {
            return;
        }

        await StopAndDisposeAsync();
    }

    public async ValueTask StopAndDisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, SiteBrowserTokens.One) != SiteBrowserTokens.Zero)
        {
            return;
        }

        try
        {
            await SiteProcessCleanup.StopAsync(Process);
        }
        finally
        {
            Process.Dispose();
        }
    }
}
