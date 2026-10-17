using Microsoft.Extensions.Options;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeCatalogRestoreVerification
{
    private const string WrongPosition = "The recovered original catalog archive position is inconsistent.";

    internal static ResourceRetirementConservationSourceOriginV1? Require(string backup, string staging, StoreIdentity originalIdentity,
        long originalPosition, IOptions<ZoneTreeStorageExecutionOptions> executionOptions,
        Action<IAtomicTransaction, StoreIdentity, long, ReadOnlyMemory<byte>>? verifyCatalog,
        ZoneTreeRestoreStagingOwnership ownership,
        Func<IKeyValueView, StoreIdentity, long, ClusterRestoreOwnerMapping>? resolveOwnerMapping,
        PhysicalShardRecord? targetOwner, CancellationToken cancellationToken)
    {
        var metadata = verifyCatalog is null ? ReadOnlyMemory<byte>.Empty
            : ZoneTreeCatalogBackupMetadataFile.ReadVerified(backup, originalIdentity,
                originalPosition, executionOptions.Value).Metadata;
        cancellationToken.ThrowIfCancellationRequested();
        ZoneTreeIdentityFile.Write(Path.Combine(staging, IdentityFileName), originalIdentity,
            executionOptions.Value.IdentityBufferBytes);
        ownership.BeginOpen();
        var options = new ZoneTreeStoreOptions(staging).ResolveExecutionOptions(executionOptions);
        var runtime = new ZoneTreeStoreRuntime(options);
        var source = new ZoneTreeStore(runtime, runtime.Identity.NodeId);
        Exception? primary = null;
        try
        {
            if (source.Position != originalPosition)
            {
                throw Errors.Fail(ErrorCode.Corruption, WrongPosition);
            }
            return source.Commit((view, _) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var proof = ZoneTreeRetirementConservation.Capture(view, options, TimeProvider.System, token: cancellationToken);
                var applied = ZoneTreeRestoreConservation.ReadApplied(view);
                verifyCatalog?.Invoke(view, originalIdentity, originalPosition, metadata);
                var mapping = resolveOwnerMapping?.Invoke(view, originalIdentity, originalPosition);
                var origin = ZoneTreeRestoreConservation.Capture(view, options, originalIdentity,
                    originalPosition, applied, proof, mapping, targetOwner, cancellationToken);
                view.ValidateCommit();
                cancellationToken.ThrowIfCancellationRequested();
                return origin;
            });
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { Dispose(source, primary, ownership); }
    }

    private static void Dispose(ZoneTreeStore source, Exception? primary,
        ZoneTreeRestoreStagingOwnership ownership)
    {
        try
        {
            source.Dispose();
            ownership.CompleteClose();
        }
        catch (Exception cleanup)
        {
            if (primary is null)
            {
                throw;
            }
            throw new AggregateException(primary, cleanup);
        }
    }
}
