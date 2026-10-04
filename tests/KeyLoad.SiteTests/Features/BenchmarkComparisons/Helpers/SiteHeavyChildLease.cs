using System.ComponentModel;
using System.Diagnostics;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteHeavyChildLease(SiteHeavyChildAdmission admission) : IDisposable
{
    private SiteHeavyChildAdmission? _admission = admission;
    private Process? _process;
    private bool _settled;

    internal void MarkStarted(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        _process = process;
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
        var owner = Interlocked.Exchange(ref _admission, null);
        owner?.Finish(_process is null || _settled);
    }
}
