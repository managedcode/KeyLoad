using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextGenerationFiles
{
    internal static void CreateOwner(string root, string leaf, Guid sourceNodeId, TextProjectionScope scope,
        DatabaseLimits limits, NativeTextGenerationSlot? first, NativeTextGenerationSlot? second,
        NativeTextGenerationSlot? third)
    {
        EnsureGenerationCapacity(root, sourceNodeId, limits, first, second, third);
        var path = Path.Combine(root, leaf);
        if (Directory.Exists(path) || File.Exists(path))
        {
            throw NativeTextErrors.Ownership();
        }
        Directory.CreateDirectory(path);
        NativeTextFileIO.VerifyDirectory(path);
        NativeTextFileIO.SetPrivateDirectoryMode(path);
        NativeTextFileIO.WriteEnvelope(Path.Combine(path, NativeTextProtocol.OwnerFile),
            new NativeTextOwnerReceipt(NativeTextProtocol.FormatVersion, root, leaf, sourceNodeId, scope,
                [new(NativeTextProtocol.NativeDirectory, true)]), 65_536);
    }

    internal static NativeTextManifest ReadManifest(string path, TextProjectionScope scope, DatabaseLimits limits)
    {
        var manifest = NativeTextFileIO.ReadEnvelope<NativeTextManifest>(path, limits.MaxQueryReadBytes);
        NativeTextValidation.ValidateManifest(manifest, scope, limits.MaxScanRecords);
        return manifest;
    }

    internal static NativeTextManifest ValidatePublished(string generationPath, string root, string leaf,
        Guid sourceNodeId, TextProjectionScope scope, DatabaseLimits limits, ReadExecutionBudget budget)
    {
        budget.Check();
        NativeTextFileIO.VerifyDirectory(generationPath);
        VerifyGenerationEntries(generationPath);
        var owner = NativeTextOwnerFiles.ReadOwner(Path.Combine(generationPath, NativeTextProtocol.OwnerFile),
            root, leaf, sourceNodeId);
        if (owner.Scope != scope || File.Exists(Path.Combine(generationPath, NativeTextProtocol.OwnerPendingFile))
            || File.Exists(Path.Combine(generationPath, NativeTextProtocol.PendingManifestFile)))
        {
            throw NativeTextErrors.Corrupt();
        }
        NativeTextInventory.ValidateTrackedLayout(generationPath, owner.OwnedPaths, budget);
        var manifest = ReadManifest(Path.Combine(generationPath, NativeTextProtocol.ManifestFile), scope, limits);
        NativeTextInventory.Verify(generationPath, owner.OwnedPaths, manifest.Files, budget);
        NativeTextFiles.CheckGenerationBound(generationPath, budget);
        budget.Check();
        return manifest;
    }

    internal static void DeleteOwned(string root, string leaf, Guid sourceNodeId, DatabaseLimits limits)
    {
        var path = Path.Combine(root, leaf);
        if (!Directory.Exists(path))
        {
            return;
        }
        ValidateGeneration(path, root, leaf, sourceNodeId, limits);
        Directory.Delete(path, recursive: true);
    }

    internal static void DeleteBuilding(string root, string leaf, Guid sourceNodeId, DatabaseLimits limits)
    {
        var path = Path.Combine(root, leaf);
        if (!Directory.Exists(path))
        {
            return;
        }
        NativeTextFileIO.VerifyDirectory(path);
        VerifyGenerationEntries(path);
        var owner = NativeTextOwnerFiles.ReadOwner(Path.Combine(path, NativeTextProtocol.OwnerFile), root, leaf,
            sourceNodeId);
        if (File.Exists(Path.Combine(path, NativeTextProtocol.OwnerPendingFile)))
        {
            throw NativeTextErrors.Corrupt();
        }
        if (File.Exists(Path.Combine(path, NativeTextProtocol.ManifestFile))
            || File.Exists(Path.Combine(path, NativeTextProtocol.PendingManifestFile)))
        {
            ValidateGeneration(path, root, leaf, sourceNodeId, limits);
        }
        else
        {
            NativeTextInventory.ValidateTrackedLayout(path, owner.OwnedPaths, allowMissingNative: true);
        }
        Directory.Delete(path, recursive: true);
    }

    internal static void ValidateGeneration(string path, string root, string leaf, Guid sourceNodeId,
        DatabaseLimits limits)
    {
        NativeTextFileIO.VerifyDirectory(path);
        VerifyGenerationEntries(path);
        var owner = NativeTextOwnerFiles.ReadOwner(Path.Combine(path, NativeTextProtocol.OwnerFile), root, leaf,
            sourceNodeId);
        var manifestPath = Path.Combine(path, NativeTextProtocol.ManifestFile);
        var pendingPath = Path.Combine(path, NativeTextProtocol.PendingManifestFile);
        if (File.Exists(manifestPath) && File.Exists(pendingPath)
            || File.Exists(Path.Combine(path, NativeTextProtocol.OwnerPendingFile)))
        {
            throw NativeTextErrors.Corrupt();
        }
        var hasInventory = File.Exists(manifestPath) || File.Exists(pendingPath);
        NativeTextInventory.ValidateTrackedLayout(path, owner.OwnedPaths, allowMissingNative: !hasInventory);
        ValidateManifestIfPresent(manifestPath, owner, path, limits);
        ValidateManifestIfPresent(pendingPath, owner, path, limits);
        NativeTextFiles.CheckGenerationBound(path);
    }

    private static void ValidateManifestIfPresent(string manifestPath, NativeTextOwnerReceipt owner,
        string generationPath, DatabaseLimits limits)
    {
        if (!File.Exists(manifestPath))
        {
            return;
        }
        var manifest = ReadManifest(manifestPath, owner.Scope!, limits);
        NativeTextInventory.Verify(generationPath, owner.OwnedPaths, manifest.Files);
    }

    internal static void VerifyGenerationEntries(string path)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var name = Path.GetFileName(entry);
            if (name is not (NativeTextProtocol.OwnerFile or NativeTextProtocol.ManifestFile
                or NativeTextProtocol.PendingManifestFile or NativeTextProtocol.OwnerPendingFile
                or NativeTextProtocol.NativeDirectory))
            {
                throw NativeTextErrors.Ownership();
            }
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0
                || (name == NativeTextProtocol.NativeDirectory) != ((attributes & FileAttributes.Directory) != 0))
            {
                throw NativeTextErrors.Ownership();
            }
        }
    }

    private static void EnsureGenerationCapacity(string root, Guid sourceNodeId, DatabaseLimits limits,
        NativeTextGenerationSlot? first, NativeTextGenerationSlot? second, NativeTextGenerationSlot? third)
    {
        NativeTextRootFiles.VerifyReceipt(root, sourceNodeId);
        var count = 0;
        var files = 0;
        long bytes = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            if (Path.GetFileName(entry) == NativeTextProtocol.RootReceiptFile)
            {
                continue;
            }
            if (++count > NativeTextPhysicalBudget.MaximumGenerations
                || !NativeTextValidation.IsGenerationLeaf(Path.GetFileName(entry))
                || !Directory.Exists(entry))
            {
                throw count > NativeTextPhysicalBudget.MaximumGenerations
                    ? NativeTextErrors.BoundExceeded() : NativeTextErrors.Ownership();
            }
            var leaf = Path.GetFileName(entry);
            var live = NativeTextLiveGenerationPreflight.Find(root, entry, leaf, sourceNodeId,
                first, second, third);
            if (live is null)
            {
                ValidateGeneration(entry, root, leaf, sourceNodeId, limits);
            }
            else
            {
                NativeTextLiveGenerationPreflight.Validate(live, entry, root, leaf, sourceNodeId, limits);
            }
            var measured = NativeTextFileIO.MeasureRegularFiles(entry, NativeTextProtocol.MaximumFiles,
                NativeTextProtocol.MaximumDiskBytes);
            if (measured.Files > NativeTextProtocol.MaximumFiles - files
                || measured.Bytes > NativeTextProtocol.MaximumDiskBytes - bytes)
            {
                throw NativeTextErrors.BoundExceeded();
            }
            files += measured.Files;
            bytes += measured.Bytes;
        }
        if (count >= NativeTextPhysicalBudget.MaximumGenerations
            || files >= NativeTextProtocol.MaximumFiles
            || bytes > NativeTextProtocol.MaximumDiskBytes - 65_536)
        {
            throw NativeTextErrors.BoundExceeded();
        }
    }
}
