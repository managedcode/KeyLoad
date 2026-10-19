using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage.ZoneTree.IO;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class AnnPendingFiles
{
    private const UnixFileMode PrivateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    internal static (AnnManifest Manifest, AnnPointer Pointer) Save(string root, AnnRootReceipt receipt,
        string leaf, AnnPendingReplay pending, IOptions<PackedAnnStorageOptions> storage,
        AnnExecutionOptions options, AnnWorkBudget budget)
    {
        AnnRootInventory.Require(root, receipt, receipt.NodeId, receipt.Incarnation, options);
        if (!receipt.OwnedGenerations.Contains(leaf, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
        }
        var directory = AnnPaths.Generation(root, leaf);
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
        var digest = Write(Path.Combine(directory, AnnProtocol.IndexFile), pending, storage.Value, options, budget);
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
        var manifestDigest = AnnReceiptFiles.Write(Path.Combine(directory, AnnProtocol.ManifestFile), manifest, options);
        AnnPaths.RequireClosedGeneration(root, leaf);
        return (manifest, new(AnnProtocol.Version, leaf, manifestDigest));
    }

    private static byte[] Write(string path, AnnPendingReplay pending, PackedAnnStorageOptions storage,
        AnnExecutionOptions options, AnnWorkBudget budget)
    {
        FileStream? file = null;
        byte[] digest;
        try
        {
            file = new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                options.FileBufferBytes, FileOptions.WriteThrough);
            digest = AnnPendingCodec.Save(file, pending, storage, budget);
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

    internal static AnnReplaySource Load(string root, AnnManifest manifest, AnnMaintenanceRequest request,
        AnnSeed current, IOptions<PackedAnnStorageOptions> storage, AnnExecutionOptions options,
        AnnSeedOptions seeds, AnnWorkBudget indexBudget, ReadExecutionBudget readBudget)
    {
        AnnCanonicalSource.RequireRestore(request, current, manifest);
        if (!manifest.IsPending || current.ProjectionCheckpoint != manifest.ReplayThrough
            || current.Cut.OutboxTail != manifest.Source.ThroughSequence || current.CorpusSha256 != manifest.Source.CorpusSha256)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProtocol.MissingDependencyHistory);
        }
        FileStream? file = null;
        AnnReplaySource replay;
        try
        {
            file = OfflineRegularFile.Open(AnnPaths.ExistingFile(root, manifest.GenerationLeaf,
                AnnProtocol.IndexFile), FileAccess.Read, FileShare.Read, options.FileBufferBytes);
            replay = AnnPendingCodec.Load(file, manifest, current, storage.Value, seeds, indexBudget, readBudget);
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
        return replay;
    }
}
