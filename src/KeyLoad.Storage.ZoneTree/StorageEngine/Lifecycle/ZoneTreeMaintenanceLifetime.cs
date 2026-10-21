using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Options;
using ZoneTree;
using ZoneTree.Core;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Owns synchronous execution maintenance; its caller owns Orleans scheduling and lifetime.</summary>
internal sealed class ZoneTreeMaintenanceLifetime : IDisposable
{
    private const long TimestampEpoch = 0;
    private readonly Lock disposalGate = new();
    private readonly IZoneTreeMaintenance<Memory<byte>, Memory<byte>> maintenance;
    private readonly ZoneTreeStorageExecutionOptions execution;
    private readonly TimeProvider clock;
    private bool disposed;
    private ExceptionDispatchInfo? originalSweepFailure;
    private ExceptionDispatchInfo? originalDisposalFailure;

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
        maintenance = tree.Maintenance;
        Swept = swept;
        Maintainer = new ZoneTreeMaintainer<Memory<byte>, Memory<byte>>(tree,
            startJobForCleaningInactiveBlockCaches: false)
        {
            BlockCacheLifeTime = execution.BlockCacheLifetime
        };
    }

    internal IMaintainer Maintainer { get; }
    internal Action<ZoneTreeMaintenanceSweep>? Swept { get; set; }

    internal ZoneTreeMaintenanceSweep Sweep()
    {
        lock (disposalGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            try
            {
                var uptime = clock.GetElapsedTime(TimestampEpoch).Ticks / TimeSpan.TicksPerMillisecond;
                var cutoff = uptime - (long)execution.BlockCacheLifetime.TotalMilliseconds;
                var result = new ZoneTreeMaintenanceSweep(maintenance.ReleaseReadBuffers(cutoff),
                    maintenance.ReleaseCircularKeyCacheRecords(), maintenance.ReleaseCircularValueCacheRecords());
                Swept?.Invoke(result);
                return result;
            }
            catch (Exception failure)
            {
                // Later sweeps keep running; the first failure is still reported by the joined close.
                originalSweepFailure ??= ExceptionDispatchInfo.Capture(failure);
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (disposalGate)
        {
            if (disposed)
            {
                originalDisposalFailure?.Throw();
                return;
            }
            disposed = true;
            var failures = new List<Exception>();
            if (originalSweepFailure is { } sweep)
            {
                failures.Add(sweep.SourceException);
            }
            ZoneTreeReadCutCleanup.Capture(Maintainer.Dispose, failures);
            try
            {
                ZoneTreeExistingStoreCleanup.ThrowFailures(failures);
            }
            catch (Exception failure)
            {
                originalDisposalFailure = ExceptionDispatchInfo.Capture(failure);
                throw;
            }
        }
    }
}
