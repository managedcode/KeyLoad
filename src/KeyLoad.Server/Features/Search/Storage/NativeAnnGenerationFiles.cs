using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage.IO;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnGenerationFiles
{
    private const UnixFileMode PrivateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    internal static (NativeAnnManifest Manifest, NativeAnnPointer Pointer) Save(string root, NativeAnnRootReceipt receipt,
        NativeAnnManifest manifest, PackedAnnIndex index,
        IOptions<PackedAnnStorageOptions> storage, NativeAnnExecutionOptions options, AnnWorkBudget budget)
    {
        NativeAnnRootInventory.Require(root, receipt, receipt.NodeId, receipt.Incarnation, options);
        if (!receipt.OwnedGenerations.Contains(manifest.GenerationLeaf, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership); }
        var directory = NativeAnnPaths.Generation(root, manifest.GenerationLeaf);
        if (Directory.Exists(directory))
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership); }
        budget.Check();
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(directory, PrivateDirectory); }
        NativeAnnPaths.RequireDirectory(directory);
        var digest = SaveIndex(Path.Combine(directory, NativeAnnProtocol.IndexFile), index, storage, options, budget);
        var completed = manifest with { IndexSha256 = digest };
        budget.Check();
        var manifestDigest = NativeAnnReceiptFiles.Write(Path.Combine(directory, NativeAnnProtocol.ManifestFile), completed, options);
        NativeAnnPaths.RequireClosedGeneration(root, manifest.GenerationLeaf);
        return (completed, new(NativeAnnProtocol.Version, manifest.GenerationLeaf, manifestDigest));
    }

    private static byte[] SaveIndex(string path, PackedAnnIndex index, IOptions<PackedAnnStorageOptions> storage,
        NativeAnnExecutionOptions options, AnnWorkBudget budget)
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
            { File.SetUnixFileMode(path, PrivateFile); }
        }
        catch (Exception primary)
        {
            try
            { file?.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
        file.Dispose();
        return digest;
    }

    internal static (NativeAnnManifest Manifest, PackedAnnIndex Index) Load(string root, NativeAnnPointer pointer,
        AnnMaintenanceRequest request, AnnSeed current, IOptions<PackedAnnOptions> policy, IOptions<PackedAnnStorageOptions> storage,
        NativeAnnExecutionOptions options, AnnWorkBudget budget)
    {
        budget.Check();
        NativeAnnPaths.RequireClosedGeneration(root, pointer.GenerationLeaf);
        var observed = NativeAnnReceiptFiles.Read<NativeAnnManifest>(
            NativeAnnPaths.ExistingFile(root, pointer.GenerationLeaf, NativeAnnProtocol.ManifestFile), options);
        NativeAnnDigest.Require(pointer.ManifestSha256, observed.Digest);
        var manifest = observed.Value;
        if (manifest.IsPending || pointer.Version != NativeAnnProtocol.Version || manifest.Version != NativeAnnProtocol.Version
            || manifest.GenerationLeaf != pointer.GenerationLeaf || manifest.Policy != policy.Value)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
        NativeAnnCanonicalSource.RequireRestore(request, current, manifest);
        var index = LoadIndex(NativeAnnPaths.ExistingFile(root, pointer.GenerationLeaf, NativeAnnProtocol.IndexFile),
            manifest.IndexSha256, policy, storage, options, budget);
        if (index.Count != manifest.Count || index.Space != manifest.Space)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
        return (manifest, index);
    }

    private static PackedAnnIndex LoadIndex(string path, byte[] digest, IOptions<PackedAnnOptions> policy,
        IOptions<PackedAnnStorageOptions> storage, NativeAnnExecutionOptions options, AnnWorkBudget budget)
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
            { file?.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
        file.Dispose();
        return index;
    }
}
