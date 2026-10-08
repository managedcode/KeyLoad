using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage.IO;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnPendingFiles
{
    private const UnixFileMode PrivateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    internal static (NativeAnnManifest Manifest, NativeAnnPointer Pointer) Save(string root, NativeAnnRootReceipt receipt,
        string leaf, NativeAnnPendingReplay pending, IOptions<PackedAnnStorageOptions> storage,
        NativeAnnExecutionOptions options, AnnWorkBudget budget)
    {
        NativeAnnRootInventory.Require(root, receipt, receipt.NodeId, receipt.Incarnation, options);
        if (!receipt.OwnedGenerations.Contains(leaf, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership); }
        var directory = NativeAnnPaths.Generation(root, leaf);
        if (Directory.Exists(directory))
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership); }
        budget.Check();
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(directory, PrivateDirectory); }
        NativeAnnPaths.RequireDirectory(directory);
        var digest = Write(Path.Combine(directory, NativeAnnProtocol.IndexFile), pending, storage.Value, options, budget);
        var manifest = pending.AdmittedUpper with
        {
            GenerationLeaf = leaf,
            Count = pending.Records.Length,
            IndexSha256 = digest,
            IsPending = true,
            ReplayAfter = pending.AfterSequence,
            ReplayThrough = pending.ThroughSequence,
            ReplayCorpusSha256 = pending.CorpusSha256,
            CheckpointIntent = pending.CheckpointIntent
        };
        var manifestDigest = NativeAnnReceiptFiles.Write(Path.Combine(directory, NativeAnnProtocol.ManifestFile), manifest, options);
        NativeAnnPaths.RequireClosedGeneration(root, leaf);
        return (manifest, new(NativeAnnProtocol.Version, leaf, manifestDigest));
    }

    private static byte[] Write(string path, NativeAnnPendingReplay pending, PackedAnnStorageOptions storage,
        NativeAnnExecutionOptions options, AnnWorkBudget budget)
    {
        FileStream? file = null;
        byte[] digest;
        try
        {
            file = new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                options.FileBufferBytes, FileOptions.WriteThrough);
            digest = NativeAnnPendingCodec.Save(file, pending, storage, budget);
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

    internal static NativeAnnReplaySource Load(string root, NativeAnnManifest manifest, AnnMaintenanceRequest request,
        AnnSeed current, IOptions<PackedAnnStorageOptions> storage, NativeAnnExecutionOptions options,
        AnnSeedOptions seeds, AnnWorkBudget indexBudget, ReadExecutionBudget readBudget)
    {
        NativeAnnCanonicalSource.RequireRestore(request, current, manifest);
        if (!manifest.IsPending || current.ProjectionCheckpoint != manifest.ReplayThrough
            || current.Cut.OutboxTail != manifest.Source.ThroughSequence || current.CorpusSha256 != manifest.Source.CorpusSha256)
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory); }
        FileStream? file = null;
        NativeAnnReplaySource replay;
        try
        {
            file = OfflineRegularFile.Open(NativeAnnPaths.ExistingFile(root, manifest.GenerationLeaf,
                NativeAnnProtocol.IndexFile), FileAccess.Read, FileShare.Read, options.FileBufferBytes);
            replay = NativeAnnPendingCodec.Load(file, manifest, current, storage.Value, seeds, indexBudget, readBudget);
        }
        catch (Exception primary)
        {
            try
            { file?.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
        file.Dispose();
        return replay;
    }
}
