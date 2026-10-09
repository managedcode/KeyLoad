using Microsoft.Extensions.Options;
using ZoneTree;
using ZoneTree.Core;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Owns native merge maintenance and one joined periodic native cache-cleanup task.</summary>
internal sealed class ZoneTreeMaintenanceLifetime : IDisposable
{
    private const long NativeTimestampEpoch = 0;
    private readonly Lock disposalGate = new();
    private readonly CancellationTokenSource stop;
    private readonly ZoneTreeStorageExecutionOptions execution;
    private readonly TimeProvider clock;
    private readonly Task worker;
    private bool disposed;

    internal ZoneTreeMaintenanceLifetime(IZoneTree<Memory<byte>, Memory<byte>> tree,
        IOptions<ZoneTreeStorageExecutionOptions> options, TimeProvider clock,
        Action<ZoneTreeMaintenanceSweep>? swept = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        execution = options.Value with { };
        execution.Validate();
        this.clock = clock;
        stop = new();
        IMaintainer? created = null;
        try
        {
            created = new ZoneTreeMaintainer<Memory<byte>, Memory<byte>>(tree,
                startJobForCleaningInactiveBlockCaches: false)
            { BlockCacheLifeTime = execution.NativeBlockCacheLifetime };
            Maintainer = created;
            using var flow = ExecutionContext.IsFlowSuppressed() ? default : ExecutionContext.SuppressFlow();
            worker = Task.Run(() => SweepAsync(tree.Maintenance, swept), CancellationToken.None);
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            try
            {
                if (created is not null)
                {
                    ZoneTreeReadCutCleanup.Capture(created.Dispose, failures);
                }
            }
            finally
            {
                stop.Dispose();
            }
            ZoneTreeExistingStoreCleanup.ThrowFailures(failures);
            throw;
        }
    }

    internal IMaintainer Maintainer { get; }
    internal Task Worker => worker;

    public void Dispose()
    {
        lock (disposalGate)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            var failures = new List<Exception>();
            try
            {
                ZoneTreeReadCutCleanup.Capture(stop.Cancel, failures);
                ZoneTreeReadCutCleanup.Capture(() => worker.GetAwaiter().GetResult(), failures);
                ZoneTreeReadCutCleanup.Capture(Maintainer.Dispose, failures);
            }
            finally
            {
                stop.Dispose();
            }
            ZoneTreeExistingStoreCleanup.ThrowFailures(failures);
        }
    }

    private async Task SweepAsync(IZoneTreeMaintenance<Memory<byte>, Memory<byte>> maintenance,
        Action<ZoneTreeMaintenanceSweep>? swept)
    {
        using var timer = new PeriodicTimer(execution.NativeMaintenanceInterval, clock);
        while (await NextTickAsync(timer).ConfigureAwait(false))
        {
            if (stop.IsCancellationRequested)
            {
                return;
            }
            var uptime = clock.GetElapsedTime(NativeTimestampEpoch).Ticks / TimeSpan.TicksPerMillisecond;
            var cutoff = uptime - (long)execution.NativeBlockCacheLifetime.TotalMilliseconds;
            var buffers = maintenance.ReleaseReadBuffers(cutoff);
            var keys = maintenance.ReleaseCircularKeyCacheRecords();
            var values = maintenance.ReleaseCircularValueCacheRecords();
            swept?.Invoke(new(buffers, keys, values));
        }
    }

    private async ValueTask<bool> NextTickAsync(PeriodicTimer timer)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return false;
        }
    }

}
