using System.ComponentModel;
using System.Diagnostics;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SiteBrowserTokens.BrowserStartupTimeoutMilliseconds);
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

internal sealed class SiteBrowserProcessStart : IAsyncDisposable
{
    public Process Process { get; }
    private readonly SiteHeavyChildLease lease;
    private int transferred;
    private int disposed;
    private int started;
    private int originalProcessExitObserved;

    private SiteBrowserProcessStart(Process process, string version, SiteHeavyChildLease lease)
    {
        Process = process;
        Version = version;
        this.lease = lease;
    }

    public string Version { get; }
    internal bool OriginalProcessExitObserved => Volatile.Read(ref originalProcessExitObserved) == SiteBrowserTokens.One;

    public static async Task<SiteBrowserProcessStart> StartAsync(string browserPath, string profilePath, string version,
        SiteHeavyChildLease lease)
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
        var owner = new SiteBrowserProcessStart(process, version, lease);
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(SiteBrowserTokens.BrowserStartFailure);
            }

            owner.MarkStarted();
            return owner;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or IOException)
        {
            try
            {
                await owner.StopAndDisposeAsync();
            }
            catch (Exception cleanupException) when (cleanupException is InvalidOperationException or Win32Exception or
                TimeoutException or ObjectDisposedException)
            {
                throw new AggregateException(exception, cleanupException);
            }
            throw;
        }
    }

    private void MarkStarted()
    {
        Volatile.Write(ref started, SiteBrowserTokens.One);
        lease.MarkStarted(Process);
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

    public async ValueTask StopAndDisposeAsync(bool sessionResourcesSettled = true)
    {
        if (Interlocked.Exchange(ref disposed, SiteBrowserTokens.One) != SiteBrowserTokens.Zero)
        {
            return;
        }

        try
        {
            if (Volatile.Read(ref started) == SiteBrowserTokens.One)
            {
                await SiteProcessCleanup.StopAsync(Process);
                Volatile.Write(ref originalProcessExitObserved, Process.HasExited ? SiteBrowserTokens.One : SiteBrowserTokens.Zero);
                if (sessionResourcesSettled)
                {
                    _ = lease.CompleteIfSettled(Task.CompletedTask, Task.CompletedTask);
                }
            }
        }
        finally
        {
            try
            {
                Process.Dispose();
            }
            finally
            {
                lease.Dispose();
            }
        }
    }
}
