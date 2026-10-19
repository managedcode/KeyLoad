namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

/// <summary>Serializes opened-store control installation against its pre-drain closure.</summary>
internal sealed class ZoneTreePointCacheLifecycle(ZoneTreeStoreRuntime runtime)
{
    private readonly Lock lifecycle = new();
    private ZoneTreePointCacheControl? control;
    private bool closing;

    internal bool IsClosing => Volatile.Read(ref closing);
    internal ZoneTreePointCache? MaintenanceCache => Volatile.Read(ref control)?.MaintenanceCache ?? runtime.EmbeddedPointCache;

    internal ZoneTreePointCacheSnapshot Snapshot()
        => Volatile.Read(ref control)?.GetDiagnostics() ?? runtime.EmbeddedPointCache?.Snapshot() ?? default;

    internal ZoneTreePointCacheReadAdmission CaptureReadAdmission()
        => Volatile.Read(ref control)?.CaptureReadAdmission() ?? new(runtime.EmbeddedPointCache);

    internal ZoneTreePointCacheControlResult TryCreate(ZoneTreePointCacheOptions options, ICacheReadPermit permit,
        out ZoneTreePointCacheControl? created)
    {
        created = null;
        if (!ZoneTreePointCacheStoreGate.TryEnterWrite(runtime, this, out var failure))
        {
            return failure;
        }

        try
        {
            if (IsClosing)
            {
                return ZoneTreePointCacheControlResult.Closed;
            }
            runtime.Check();
            lock (lifecycle)
            {
                if (closing)
                {
                    return ZoneTreePointCacheControlResult.Closed;
                }
                if (control is not null || runtime.EmbeddedPointCache is not null)
                {
                    return ZoneTreePointCacheControlResult.AlreadyConfigured;
                }

                created = new(runtime, options, permit);
                Volatile.Write(ref control, created);
                return ZoneTreePointCacheControlResult.Created;
            }
        }
        finally
        {
            runtime.Gate.ExitWriteLock();
        }
    }

    internal ZoneTreePointCacheControlResult TryApply(ZoneTreePointCacheControlState state,
        CacheReadPermitAcceptance acceptance)
    {
        if (state.Snapshot().Closed)
        {
            return ZoneTreePointCacheControlResult.Closed;
        }
        if (!ZoneTreePointCacheStoreGate.TryEnterWrite(runtime, this, out var failure))
        {
            return failure;
        }

        try
        {
            if (IsClosing)
            {
                return ZoneTreePointCacheControlResult.Closed;
            }
            runtime.Check();
            return state.ApplyUnderWrite(acceptance);
        }
        finally
        {
            runtime.Gate.ExitWriteLock();
        }
    }

    internal bool TryBeginClose(out ZoneTreePointCacheControl? captured)
    {
        lock (lifecycle)
        {
            captured = control;
            if (closing)
            {
                return false;
            }

            Volatile.Write(ref closing, true);
            return true;
        }
    }

    internal void DisableAdmission()
    {
        var current = Volatile.Read(ref control);
        if (current is not null)
        {
            current.CloseAdmission();
        }
        else
        {
            runtime.EmbeddedPointCache?.Disable();
        }
    }

    internal void DisposeCacheUnderWrite()
    {
        var current = Volatile.Read(ref control);
        if (current is not null)
        {
            current.DisposeCacheUnderWrite();
        }
        else
        {
            runtime.EmbeddedPointCache?.Dispose();
        }
    }
}
