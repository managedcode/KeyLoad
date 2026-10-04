namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

/// <summary>Controls optional point-value admission for one actual opened storage runtime.</summary>
/// <remarks>This process-local owner grants no read authority and provides no RF3 readiness certificate.</remarks>
public sealed class ZoneTreePointCacheControl
{
    private readonly ZoneTreeStoreRuntime runtime;
    private readonly ZoneTreePointCacheControlState state;

    internal ZoneTreePointCacheControl(ZoneTreeStoreRuntime runtime, ZoneTreePointCacheOptions options, ICacheReadPermit permit)
    {
        this.runtime = runtime;
        state = new(options, permit);
        RuntimeId = Guid.NewGuid();
    }

    /// <summary>Gets this nonpersisted local configuration generation, excluding physical readiness claims.</summary>
    public Guid RuntimeId { get; }

    /// <summary>Attempts a zero-wait actual physical owner and health observation.</summary>
    /// <param name="identity">Actual nonsecret identities for Healthy; default for failure or closure.</param>
    /// <returns>Healthy, Busy or Closed; native recovery failures remain exceptions.</returns>
    /// <remarks>Healthy is an observation, not full silo readiness, read authority or remote lease eligibility.</remarks>
    public ZoneTreePointCacheOwnerStatus TryReadOwnerIdentity(out ZoneTreePointCacheOwnerIdentity identity)
        => ZoneTreePointCacheOwnerObservation.TryRead(runtime, state, RuntimeId, out identity);

    /// <summary>Attempts exact eligible binding under a zero-wait physical storage writer.</summary>
    /// <param name="acceptance">Complete local receipt returned by this owner's fixed permit.</param>
    /// <returns>Applied, AlreadyApplied, Busy, Rejected, Unavailable or Closed.</returns>
    /// <remarks>Zero wait applies to writer acquisition; subsequent locks and allocations can wait.</remarks>
    public ZoneTreePointCacheControlResult TryApply(CacheReadPermitAcceptance acceptance)
        => runtime.CacheLifecycle.TryApply(state, acceptance);

    /// <summary>Retires a provably stale binding at or before an actually withdrawn positive revision.</summary>
    /// <param name="withdrawnRevision">Revision successfully withdrawn by the trusted local receiver first.</param>
    /// <returns>True only when a new retired state is published; stale or repeated retirement returns false.</returns>
    public bool Retire(long withdrawnRevision) => state.Retire(withdrawnRevision);

    /// <summary>Permanently closes admission before retiring entries, without waiting for storage readers.</summary>
    /// <remarks>Owned index, active pins and fills stay charged until actual release.</remarks>
    public void CloseAdmission() => state.CloseAdmission();

    /// <summary>Observes effective eligibility and current-helper counters, which reset on cold replacement.</summary>
    /// <returns>Modeled ownership without keys, payloads, credentials or paths.</returns>
    /// <remarks>No-helper counters are zero; cumulative logical reads remain in the store diagnostics.</remarks>
    public ZoneTreePointCacheSnapshot GetDiagnostics() => state.Snapshot();

    internal ZoneTreePointCache? MaintenanceCache => state.MaintenanceCache;
    internal ZoneTreePointCacheReadAdmission CaptureReadAdmission() => state.CaptureReadAdmission();
    internal void DisposeCacheUnderWrite() => state.DisposeUnderWrite();
}
