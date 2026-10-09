using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Continues one original admitted native slot without deleting retained source or generating a new identity.</summary>
internal static class ZoneTreeRestoreSlotDriver
{
    private const string Invalid = "The native restore slot identity or publication is inconsistent.";
    private const string TerminalCutLost = "The published restore target no longer has its original terminal native cut.";

    internal static NativeCatalogRestoreSlotCompletion Restore(string backup, string destination, string stage,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions, TimeProvider clock, ClusterRestoreSlotContext context,
        ReadOnlyMemory<byte> signingKey,
        Action<IAtomicTransaction, StoreIdentity, long, ReadOnlyMemory<byte>, ClusterRestoreSlotContext> reconcile,
        Action<IKeyValueView, StoreIdentity, long, ClusterRestoreSlotContext> verify, Action<NativeClusterRestoreStage>? observer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reconcile);
        ArgumentNullException.ThrowIfNull(verify);
        ClusterRestoreSlotContextValidation.Require(context);
        var policy = executionOptions.Value;
        policy.Validate();
        var original = ZoneTreeRestoreSlotSource.Require(backup, context, policy, ct, out var manifest);
        var sourceFiles = ZoneTreeRestoreSlotSource.Describe(backup, policy, ct);
        var sourceMetadata = ZoneTreeCatalogBackupMetadataFile.ReadVerified(backup, original,
            context.SourcePosition, policy).Metadata;
        destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        stage = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stage));
        if (stage == destination || context.TargetNodeId == Guid.Empty || context.TargetIncarnation == original.Incarnation
            || signingKey.Length != SigningKeyBytes || context.TargetNodeId == original.NodeId
            || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(signingKey.Span)),
                context.TargetSignerFingerprint, StringComparison.Ordinal)
            || CryptographicOperations.FixedTimeEquals(original.SigningKey.Span, signingKey.Span))
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        var published = Directory.Exists(destination);
        var ownedRoot = published ? destination : stage;
        var owner = ZoneTreeRestoreSlotAdmissionOwner.Acquire(ownedRoot, context, sourceFiles, policy, clock, requireExisting: false, ct);
        Exception? primary = null;
        var cleanupCompleted = false;
        try
        {
            try
            {
                var completion = Continue(backup, ownedRoot, executionOptions, context, original, signingKey,
                    manifest, sourceMetadata, reconcile, verify, published, observer, ct);
                ct.ThrowIfCancellationRequested();
                PublishJoined(published, stage, destination);
                return completion;
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

    private static NativeCatalogRestoreSlotCompletion Continue(string backup, string root,
        IOptions<ZoneTreeStorageExecutionOptions> options, ClusterRestoreSlotContext context,
        StoreIdentity original, ReadOnlyMemory<byte> signingKey, ZoneTreeBackupRestoreManifest manifest,
        ReadOnlyMemory<byte> metadata,
        Action<IAtomicTransaction, StoreIdentity, long, ReadOnlyMemory<byte>, ClusterRestoreSlotContext> reconcile,
        Action<IKeyValueView, StoreIdentity, long, ClusterRestoreSlotContext> verify, bool published, Action<NativeClusterRestoreStage>? observer, CancellationToken ct)
    {
        var policy = options.Value;
        var identityPath = Path.Combine(root, IdentityFileName);
        var journal = manifest.Files.Single(file => file.Name == JournalFileName);
        if (!File.Exists(identityPath))
        {
            if (published)
            { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
            ZoneTreeRestoreSlotPrefix.Continue(backup, root, journal, policy, ct);
            observer?.Invoke(NativeClusterRestoreStage.SourcePrefixPersisted);
            ZoneTreeIdentityFile.Write(identityPath, original, policy.IdentityBufferBytes);
        }
        ZoneTreeRestoreSlotPrefix.RequireOriginalPrefix(backup, root, journal, policy, ct);
        ZoneTreeStoreRuntime? runtime = null;
        ZoneTreeStore? store = null;
        Exception? primary = null;
        var cleanupCompleted = false;
        try
        {
            try
            {
                runtime = new ZoneTreeStoreRuntime(new ZoneTreeStoreOptions(root).ResolveExecutionOptions(options));
                store = new ZoneTreeStore(runtime, runtime.Identity.NodeId);
                runtime = null; // The actual successful store constructor takes runtime ownership.

                var sourceIdentity = store.Identity.NodeId == original.NodeId && store.Identity.Incarnation == original.Incarnation;
                if (sourceIdentity && published)
                { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
                if (sourceIdentity)
                {
                    ZoneTreeRestoreSlotTransactions.Continue(store, original, context, metadata, reconcile, verify, observer, ct);
                    var target = original with
                    {
                        NodeId = context.TargetNodeId,
                        Incarnation = context.TargetIncarnation,
                        SigningKey = signingKey.ToArray(),
                        DispatchPaused = true
                    };
                    // Join the original native store BEFORE publishing its admitted target identity.
                    return CompleteAfterReopen(store, target, root, options, context, signingKey, verify, observer, ct);
                }
                ZoneTreeRestoreSlotState.RequireTarget(store.Identity, context, signingKey);
                ZoneTreeRestoreSlotTransactions.Continue(store, original, context, metadata, reconcile, verify, observer, ct);
                ZoneTreeRestoreSlotTransactions.Reset(store, context, verify, observer, ct);
                return Observe(store, context, signingKey, verify, ct);
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

    private static NativeCatalogRestoreSlotCompletion CompleteAfterReopen(ZoneTreeStore original, StoreIdentity targetIdentity, string root,
        IOptions<ZoneTreeStorageExecutionOptions> options, ClusterRestoreSlotContext context,
        ReadOnlyMemory<byte> signingKey,
        Action<IKeyValueView, StoreIdentity, long, ClusterRestoreSlotContext> verify, Action<NativeClusterRestoreStage>? observer, CancellationToken ct)
    {
        original.Dispose();
        ct.ThrowIfCancellationRequested();
        ZoneTreeIdentityFile.Write(Path.Combine(root, IdentityFileName), targetIdentity, options.Value.IdentityBufferBytes);
        observer?.Invoke(NativeClusterRestoreStage.IdentityPublished);
        ZoneTreeStoreRuntime? runtime = null;
        ZoneTreeStore? target = null;
        Exception? primary = null;
        var cleanupCompleted = false;
        try
        {
            try
            {
                runtime = new ZoneTreeStoreRuntime(new ZoneTreeStoreOptions(root).ResolveExecutionOptions(options));
                target = new ZoneTreeStore(runtime, runtime.Identity.NodeId);
                runtime = null; // The actual successful store constructor takes runtime ownership.

                ZoneTreeRestoreSlotState.RequireTarget(target.Identity, context, signingKey);
                ZoneTreeRestoreSlotTransactions.Reset(target, context, verify, observer, ct);
                return Observe(target, context, signingKey, verify, ct);
            }
            catch (Exception failure) { primary = failure; throw; }
            finally { target?.Dispose(); runtime?.Dispose(); cleanupCompleted = true; }
        }
        catch (Exception terminal)
        {
            if (primary is not null && !cleanupCompleted)
            { throw new AggregateException(primary, terminal); }
            throw;
        }
    }

    private static void PublishJoined(bool published, string stage, string destination)
    {
        if (published)
        { return; }
        if (Directory.Exists(destination) || File.Exists(destination))
        { throw Errors.Fail(ErrorCode.Conflict, Invalid); }
        // The CLI's exclusive operation owner spans all joined stores and checked publication.
        Directory.Move(stage, destination);
    }

    internal static NativeCatalogRestoreSlotCompletion Observe(ZoneTreeStore store, ClusterRestoreSlotContext context,
        ReadOnlyMemory<byte> signingKey,
        Action<IKeyValueView, StoreIdentity, long, ClusterRestoreSlotContext> verify, CancellationToken ct)
        => store.Read(view =>
        {
            ct.ThrowIfCancellationRequested();
            ZoneTreeRestoreSlotState.RequireTarget(store.Identity, context, signingKey);
            var reconciled = ZoneTreeRestoreSlotState.Require(view, context,
                ClusterRestoreSlotCommitKind.Reconciled, store.Position);
            var reset = ZoneTreeRestoreSlotState.Require(view, context,
                ClusterRestoreSlotCommitKind.AuthorityReset, store.Position);
            if (reset.MappingsDigest != reconciled.MappingsDigest)
            { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
            if (reset.NativeCommitPosition != store.Position)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, TerminalCutLost); }
            verify(view, store.Identity, store.Position, context);
            return new NativeCatalogRestoreSlotCompletion(NativeCatalogRestoreSlotCompletion.CurrentVersion,
                context, store.Identity.NodeId, store.Identity.Incarnation, context.TargetSignerFingerprint,
                reconciled.NativeCommitPosition, reset.NativeCommitPosition, store.Position);
        });
}
