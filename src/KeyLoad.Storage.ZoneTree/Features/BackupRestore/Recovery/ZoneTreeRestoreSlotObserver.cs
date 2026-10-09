using Microsoft.Extensions.Options;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Observation-only reopening of a genuinely completed original slot, never reconstruction or reset.</summary>
internal static class ZoneTreeRestoreSlotObserver
{
    private const string Invalid = "The completed original restore slot is absent or changed.";

    internal static NativeCatalogRestoreSlotCompletion Require(string backup, string destination,
        IOptions<ZoneTreeStorageExecutionOptions> options, TimeProvider clock, ClusterRestoreSlotContext context,
        ReadOnlyMemory<byte> signingKey, Action<IKeyValueView, StoreIdentity, long, ClusterRestoreSlotContext> verify,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(verify);
        ClusterRestoreSlotContextValidation.Require(context);
        var policy = options.Value;
        policy.Validate();
        _ = ZoneTreeRestoreSlotSource.Require(backup, context, policy, ct, out _);
        var sourceFiles = ZoneTreeRestoreSlotSource.Describe(backup, policy, ct);
        if (!Directory.Exists(destination))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        var owner = ZoneTreeRestoreSlotAdmissionOwner.Acquire(destination, context, sourceFiles, policy, clock, requireExisting: true, ct);
        Exception? primary = null;
        var cleanupCompleted = false;
        try
        {
            try
            {
                return ReadOwnedStore(destination, options, context, signingKey, verify, ct);
            }
            catch (Exception failure) { primary = failure; throw; }
            finally { owner.Dispose(); cleanupCompleted = true; }
        }
        catch (Exception terminal)
        {
            if (primary is not null && !cleanupCompleted)
            { throw new AggregateException(primary, terminal); }
            throw;
        }
    }

    private static NativeCatalogRestoreSlotCompletion ReadOwnedStore(string destination,
        IOptions<ZoneTreeStorageExecutionOptions> options, ClusterRestoreSlotContext context,
        ReadOnlyMemory<byte> signingKey, Action<IKeyValueView, StoreIdentity, long, ClusterRestoreSlotContext> verify,
        CancellationToken ct)
    {
        ZoneTreeStoreRuntime? runtime = null;
        ZoneTreeStore? store = null;
        Exception? primary = null;
        var cleanupCompleted = false;
        try
        {
            try
            {
                runtime = new ZoneTreeStoreRuntime(new ZoneTreeStoreOptions(destination).ResolveExecutionOptions(options));
                store = new ZoneTreeStore(runtime, runtime.Identity.NodeId);
                runtime = null; // The actual successful store constructor takes runtime ownership.
                return ZoneTreeRestoreSlotDriver.Observe(store, context, signingKey, verify, ct);
            }
            catch (Exception failure) { primary = failure; throw; }
            finally { store?.Dispose(); runtime?.Dispose(); cleanupCompleted = true; }
        }
        catch (Exception terminal)
        {
            if (primary is not null && !cleanupCompleted)
            { throw new AggregateException(primary, terminal); }
            throw;
        }
    }

}
