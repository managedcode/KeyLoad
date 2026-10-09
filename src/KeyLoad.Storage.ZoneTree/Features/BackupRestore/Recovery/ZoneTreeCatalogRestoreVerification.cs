using Microsoft.Extensions.Options;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeCatalogRestoreVerification
{
    private const string WrongPosition = "The recovered original catalog archive position is inconsistent.";

    internal static void Require(string backup, string staging, StoreIdentity originalIdentity,
        long originalPosition, IOptions<ZoneTreeStorageExecutionOptions> executionOptions,
        Action<IAtomicTransaction, StoreIdentity, long, ReadOnlyMemory<byte>> verifyCatalog,
        CancellationToken cancellationToken)
    {
        var metadata = ZoneTreeCatalogBackupMetadataFile.ReadVerified(backup, originalIdentity,
            originalPosition, executionOptions.Value);
        cancellationToken.ThrowIfCancellationRequested();
        ZoneTreeIdentityFile.Write(Path.Combine(staging, IdentityFileName), originalIdentity,
            executionOptions.Value.IdentityBufferBytes);
        var runtime = new ZoneTreeStoreRuntime(new ZoneTreeStoreOptions(staging).ResolveExecutionOptions(executionOptions));
        var source = new ZoneTreeStore(runtime, runtime.Identity.NodeId);
        Exception? primary = null;
        try
        {
            if (source.Position != originalPosition)
            { throw Errors.Fail(ErrorCode.Corruption, WrongPosition); }
            source.Commit((view, _) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                verifyCatalog(view, originalIdentity, originalPosition, metadata.Metadata);
                view.ValidateCommit();
                cancellationToken.ThrowIfCancellationRequested();
                return true;
            });
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { Dispose(source, primary); }
    }

    private static void Dispose(ZoneTreeStore source, Exception? primary)
    {
        try
        { source.Dispose(); }
        catch (Exception cleanup)
        {
            if (primary is null)
            { throw; }
            throw new AggregateException(primary, cleanup);
        }
    }
}
