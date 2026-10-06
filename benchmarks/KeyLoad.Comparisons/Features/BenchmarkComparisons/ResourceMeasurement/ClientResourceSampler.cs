using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

// Measures the load generator, including its drivers and sampler. These are never database CPU/RSS.
internal sealed class ClientResourceSampler : IAsyncDisposable
{
    private readonly NativeComparisonExecutionOptions settings;
    private const int NoResources = 0;
    private readonly System.Threading.Lock gate = new();
    private readonly Process process = Process.GetCurrentProcess();
    private readonly CancellationTokenSource lifetime = new();
    private readonly TimeSpan cpu;
    private readonly long allocated;
    private readonly Task sampler;
    private long peak;

    public ClientResourceSampler(IOptions<NativeComparisonExecutionOptions> options)
    {
        settings = options.Value;
        settings.Validate();
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
                await Task.Delay(settings.ResourceSampleIntervalMilliseconds, lifetime.Token);
                lock (gate)
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
        lock (gate)
        { process.Refresh(); elapsedCpu = process.TotalProcessorTime - cpu; }
        await lifetime.CancelAsync();
        await sampler;
        process.Refresh();
        return new(Math.Max(NoResources, elapsedCpu.TotalSeconds), Math.Max(NoResources, bytes), Math.Max(peak, process.WorkingSet64),
            settings.ResourceSampleIntervalMilliseconds);
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        await sampler;
        lifetime.Dispose();
        process.Dispose();
    }
}
