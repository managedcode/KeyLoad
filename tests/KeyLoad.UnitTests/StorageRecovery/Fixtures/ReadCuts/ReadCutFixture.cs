using System.Runtime.ExceptionServices;
using System.Text;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class ReadCutFixture : IDisposable
{
    private const string DirectoryPrefix = "keyload-execution-read-cut-";
    private const string Prefix = "doc/";
    private const string AcquisitionUnjoined = "An original execution read-cut owner acquisition is unknown; retain its root.";
    private readonly List<ZoneTreeStore> opened = [];
    private readonly List<StorageOwnerSettlement> attempts = [];
    private readonly TimeProvider clock;
    private ExceptionDispatchInfo? originalAcquisitionFailure;

    internal string DirectoryPath { get; } = Path.Combine(AppContext.BaseDirectory, DirectoryPrefix + Guid.NewGuid().ToString("N"));
    internal ZoneTreeStore Store
    {
        get; private set;
    }

    internal ReadCutFixture(TimeProvider? timeProvider = null)
    {
        clock = timeProvider ?? TimeProvider.System;
        Store = Open();
    }

    internal ZoneTreeReadCutLease Capture(ZoneTreeReadCutLimits limits, CancellationToken token = default)
        => Store.Read(view => Store.CaptureReadCut(view, limits, token));

    internal void Reopen()
    {
        Store.Dispose();
        Store = Open();
    }

    internal static byte[] Key(string suffix) => Encoding.UTF8.GetBytes(Prefix + suffix);
    internal static byte[] Value(string value) => Encoding.UTF8.GetBytes(value);
    internal static ZoneTreeReadCutLimits Limits(int records, long bytes, TimeSpan? elapsed = null)
        => new(records, bytes, elapsed ?? TimeSpan.FromMinutes(1));

    public void Dispose()
    {
        var failures = new List<Exception>();
        foreach (var store in opened)
        {
            KeyLoad.Server.ServerFailureObserver.Observe(store.Dispose, failures);
        }
        if (failures.Count == 0 && attempts.Any(attempt => !attempt.IsReleased))
        {
            failures.Add(originalAcquisitionFailure?.SourceException ?? new InvalidOperationException(AcquisitionUnjoined));
        }
        if (failures.Count == 0)
        {
            KeyLoad.Server.ServerFailureObserver.Observe(() => Directory.Delete(DirectoryPath, recursive: true), failures);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private ZoneTreeStore Open()
    {
        if (attempts.Any(attempt => !attempt.IsReleased))
        {
            originalAcquisitionFailure?.Throw();
            throw new InvalidOperationException(AcquisitionUnjoined);
        }
        var options = new ZoneTreeStoreOptions(DirectoryPath);
        var execution = UnitExecutionOptions.StorageExecution();
        var cache = UnitExecutionOptions.PointCacheExecution();
        var attempt = new StorageOwnerSettlement();
        attempts.Add(attempt);
        try
        {
            return Publish(ZoneTreeStore.OpenOwned(options, execution, cache, attempt, clock));
        }
        catch (Exception failure)
        {
            originalAcquisitionFailure = ExceptionDispatchInfo.Capture(failure);
            throw;
        }
    }

    private ZoneTreeStore Publish(ZoneTreeStore store)
    {
        try
        {
            opened.Add(store);
            return store;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            try
            {
                store.Dispose();
            }
            catch (Exception failure) when (KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

}
