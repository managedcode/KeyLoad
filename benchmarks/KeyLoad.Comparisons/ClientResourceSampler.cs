using System.Diagnostics;

namespace KeyLoad.Comparisons;

// Measures the load generator, including its drivers and sampler. These are never database CPU/RSS.
internal sealed class ClientResourceSampler : IAsyncDisposable
{
    private const int IntervalMs = 50;
    private readonly Process process = Process.GetCurrentProcess();
    private readonly CancellationTokenSource lifetime = new();
    private readonly TimeSpan cpu;
    private readonly long allocated;
    private readonly Task sampler;
    private long peak;

    public ClientResourceSampler()
    {
        process.Refresh();
        peak = process.WorkingSet64;
        cpu = process.TotalProcessorTime;
        allocated = GC.GetTotalAllocatedBytes(precise: true);
        sampler = SampleAsync();
    }

    private async Task SampleAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(IntervalMs, lifetime.Token);
                lock (process)
                {
                    process.Refresh();
                    peak = Math.Max(peak, process.WorkingSet64);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    public async Task<ClientResources> StopAsync()
    {
        var bytes = GC.GetTotalAllocatedBytes(precise: true) - allocated;
        TimeSpan elapsedCpu;
        lock (process) { process.Refresh(); elapsedCpu = process.TotalProcessorTime - cpu; }
        await lifetime.CancelAsync();
        await sampler;
        process.Refresh();
        return new(Math.Max(0, elapsedCpu.TotalSeconds), Math.Max(0, bytes), Math.Max(peak, process.WorkingSet64), IntervalMs);
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync(); await sampler;
        lifetime.Dispose(); process.Dispose();
    }
}
