using System.ComponentModel;
using System.Diagnostics;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteHeavyChildLease(SiteHeavyChildAdmission admission) : IDisposable
{
    private SiteHeavyChildAdmission? _admission = admission;
    private Process? _process;
    private bool _settled;
    private int transferredToCaller;

    internal void TransferToCaller() => Interlocked.Exchange(ref transferredToCaller, SiteBrowserTokens.One);

    internal void MarkStarted(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (_process is not null && !_settled)
        {
            throw new InvalidOperationException(SiteHeavyChildTokens.UnsafeOwnership);
        }

        _process = process;
        _settled = false;
        _ = process.Id;
    }

    internal bool CompleteIfSettled(Task stdout, Task stderr)
    {
        try
        {
            _settled = _process is { HasExited: true } && stdout.IsCompleted && stderr.IsCompleted;
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception)
        {
            _settled = false;
        }

        return _settled;
    }

    internal static async Task StopAndObserveAsync(Process process, Task<string> stdout,
        Task<string> stderr, SiteHeavyChildLease? lease)
    {
        try
        {
            await SiteProcessCleanup.StopAsync(process);
        }
        finally
        {
            try
            {
                await SiteProcessCleanup.ObserveCapturesAsync(process, stdout, stderr);
            }
            finally
            {
                _ = lease?.CompleteIfSettled(stdout, stderr);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref transferredToCaller, SiteBrowserTokens.Zero) == SiteBrowserTokens.One)
        { return; }
        var owner = Interlocked.Exchange(ref _admission, null);
        owner?.Finish(_process is null || _settled);
    }
}
