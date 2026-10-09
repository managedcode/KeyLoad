using Microsoft.Extensions.Options;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Reads actual verified archived native state without inventing a database owner or mutating the archive.</summary>
internal static class ZoneTreeCatalogArchiveView
{
    private const string Prefix = "keyload-catalog-archive-view-";
    private const string GuidFormat = "N";
    private const string Invalid = "The verified archive view is inconsistent with its original cut.";

    internal static T Read<T>(string backup, IOptions<ZoneTreeStorageExecutionOptions> executionOptions,
        Func<IKeyValueView, StoreIdentity, long, ReadOnlyMemory<byte>, T> read, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(executionOptions);
        var policy = executionOptions.Value;
        policy.Validate();
        var stage = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString(GuidFormat));
        ZoneTreeStore? store = null;
        ZoneTreeStoreRuntime? runtime = null;
        Exception? primary = null;
        var cleanupCompleted = false;
        try
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                ZoneTreeStoreFiles.CreatePrivateDirectory(stage);
                var identity = ZoneTreeBackupRestoreFiles.ReadAndVerify(backup, stage, policy, out var position);
                var metadata = ZoneTreeCatalogBackupMetadataFile.ReadVerified(backup, identity, position, policy);
                ZoneTreeIdentityFile.Write(Path.Combine(stage, IdentityFileName), identity, policy.IdentityBufferBytes);
                runtime = new ZoneTreeStoreRuntime(new ZoneTreeStoreOptions(stage).ResolveExecutionOptions(executionOptions));
                store = new(runtime, runtime.Identity.NodeId);
                runtime = null; // Ownership transfers only after the actual store constructor succeeds.
                if (store.Position != position)
                { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
                var result = store.Read(view =>
                {
                    ct.ThrowIfCancellationRequested();
                    return read(view, store.Identity, store.Position, metadata.Metadata);
                });
                ct.ThrowIfCancellationRequested();
                return result;
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                store?.Dispose();
                runtime?.Dispose();
                // Only genuinely joined native handles permit deleting this owned temporary view.
                if (Directory.Exists(stage))
                { Directory.Delete(stage, recursive: true); }
                cleanupCompleted = true;
            }
        }
        catch (Exception terminal)
        {
            if (primary is not null && !cleanupCompleted)
            { throw new AggregateException(primary, terminal); }
            throw;
        }
    }
}
