using KeyLoad.Core.Search;
using KeyLoad.Query.Search;
using KeyLoad.Storage.ZoneTree.IO;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class AnnGenerationFiles
{
    private const UnixFileMode PrivateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    internal static (AnnManifest Manifest, AnnPointer Pointer) Save(string root, AnnRootReceipt receipt,
        AnnManifest manifest, PackedAnnIndex index,
        IOptions<PackedAnnStorageOptions> storage, AnnExecutionOptions options, AnnWorkBudget budget)
    {
        AnnRootInventory.Require(root, receipt, receipt.NodeId, receipt.Incarnation, options);
        if (!receipt.OwnedGenerations.Contains(manifest.GenerationLeaf, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
        }
        var directory = AnnPaths.Generation(root, manifest.GenerationLeaf);
        if (Directory.Exists(directory))
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
        }
        budget.Check();
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, PrivateDirectory);
        }
        AnnPaths.RequireDirectory(directory);
        var digest = SaveIndex(Path.Combine(directory, AnnProtocol.IndexFile), index, storage, options, budget);
        var completed = manifest with
        {
            IndexSha256 = digest
        };
        budget.Check();
        var manifestDigest = AnnReceiptFiles.Write(Path.Combine(directory, AnnProtocol.ManifestFile), completed, options);
        AnnPaths.RequireClosedGeneration(root, manifest.GenerationLeaf);
        return (completed, new(AnnProtocol.Version, manifest.GenerationLeaf, manifestDigest));
    }

    private static byte[] SaveIndex(string path, PackedAnnIndex index, IOptions<PackedAnnStorageOptions> storage,
        AnnExecutionOptions options, AnnWorkBudget budget)
    {
        FileStream? file = null;
        byte[] digest;
        try
        {
            file = new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                options.FileBufferBytes, FileOptions.WriteThrough);
            digest = index.Save(file, storage, budget);
            file.Flush(flushToDisk: true);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, PrivateFile);
            }
        }
        catch (Exception primary)
        {
            try
            {
                file?.Dispose();
            }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
        file.Dispose();
        return digest;
    }

    internal static (AnnManifest Manifest, PackedAnnIndex Index) Load(string root, AnnPointer pointer,
        AnnMaintenanceRequest request, AnnSeed current, IOptions<PackedAnnOptions> policy, IOptions<PackedAnnStorageOptions> storage,
        AnnExecutionOptions options, AnnWorkBudget budget)
    {
        budget.Check();
        AnnPaths.RequireClosedGeneration(root, pointer.GenerationLeaf);
        var observed = AnnReceiptFiles.Read<AnnManifest>(
            AnnPaths.ExistingFile(root, pointer.GenerationLeaf, AnnProtocol.ManifestFile), options);
        AnnDigest.Require(pointer.ManifestSha256, observed.Digest);
        var manifest = observed.Value;
        if (manifest.IsPending || pointer.Version != AnnProtocol.Version || manifest.Version != AnnProtocol.Version
            || manifest.GenerationLeaf != pointer.GenerationLeaf || manifest.Policy != policy.Value)
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt);
        }
        AnnCanonicalSource.RequireRestore(request, current, manifest);
        var index = LoadIndex(AnnPaths.ExistingFile(root, pointer.GenerationLeaf, AnnProtocol.IndexFile),
            manifest.IndexSha256, policy, storage, options, budget);
        if (index.Count != manifest.Count || index.Space != manifest.Space)
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt);
        }
        return (manifest, index);
    }

    private static PackedAnnIndex LoadIndex(string path, byte[] digest, IOptions<PackedAnnOptions> policy,
        IOptions<PackedAnnStorageOptions> storage, AnnExecutionOptions options, AnnWorkBudget budget)
    {
        FileStream? file = null;
        PackedAnnIndex index;
        try
        {
            file = OfflineRegularFile.Open(path, FileAccess.Read, FileShare.Read, options.FileBufferBytes);
            index = PackedAnnIndex.Load(file, digest, policy, storage, budget);
        }
        catch (Exception primary)
        {
            try
            {
                file?.Dispose();
            }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
        file.Dispose();
        return index;
    }
}
