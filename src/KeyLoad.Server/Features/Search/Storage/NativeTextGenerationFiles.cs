using Microsoft.Extensions.Options;
using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextGenerationFiles
{
    internal static void CreateOwner(string root, string leaf, Guid sourceNodeId, TextProjectionScope scope, DatabaseLimits limits, NativeTextGenerationSlot? first, NativeTextGenerationSlot? second, NativeTextGenerationSlot? third, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        EnsureGenerationCapacity(root, sourceNodeId, limits, first, second, third, executionOptions: executionOptions);
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
                [new(NativeTextProtocol.NativeDirectory, true)]), executionOptions.Value.MaximumOwnerReceiptBytes, executionOptions: executionOptions);
    }

    internal static NativeTextManifest ReadManifest(string path, TextProjectionScope scope, DatabaseLimits limits, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        var manifest = NativeTextFileIO.ReadEnvelope<NativeTextManifest>(path, limits.MaxQueryReadBytes);
        NativeTextValidation.ValidateManifest(manifest, scope, limits.MaxScanRecords, executionOptions: executionOptions);
        return manifest;
    }

    internal static NativeTextManifest ValidatePublished(string generationPath, string root, string leaf, Guid sourceNodeId, TextProjectionScope scope, DatabaseLimits limits, ReadExecutionBudget budget, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        budget.Check();
        NativeTextFileIO.VerifyDirectory(generationPath);
        VerifyGenerationEntries(generationPath);
        var owner = NativeTextOwnerFiles.ReadOwner(Path.Combine(generationPath, NativeTextProtocol.OwnerFile),
            root, leaf, sourceNodeId, executionOptions: executionOptions);
        if (owner.Scope != scope || File.Exists(Path.Combine(generationPath, NativeTextProtocol.OwnerPendingFile))
            || File.Exists(Path.Combine(generationPath, NativeTextProtocol.PendingManifestFile)))
        {
            throw NativeTextErrors.Corrupt();
        }
        NativeTextInventory.ValidateTrackedLayout(generationPath, owner.OwnedPaths, budget: budget, executionOptions: executionOptions);
        var manifest = ReadManifest(Path.Combine(generationPath, NativeTextProtocol.ManifestFile), scope, limits, executionOptions: executionOptions);
        NativeTextInventory.Verify(generationPath, owner.OwnedPaths, manifest.Files, budget: budget, executionOptions: executionOptions);
        NativeTextFiles.CheckGenerationBound(generationPath, budget: budget, executionOptions: executionOptions);
        budget.Check();
        return manifest;
    }

    internal static void DeleteOwned(string root, string leaf, Guid sourceNodeId, DatabaseLimits limits, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        var path = Path.Combine(root, leaf);
        if (!Directory.Exists(path))
        {
            return;
        }
        ValidateGeneration(path, root, leaf, sourceNodeId, limits, executionOptions: executionOptions);
        Directory.Delete(path, recursive: true);
    }

    internal static void DeleteBuilding(string root, string leaf, Guid sourceNodeId, DatabaseLimits limits, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        var path = Path.Combine(root, leaf);
        if (!Directory.Exists(path))
        {
            return;
        }
        NativeTextFileIO.VerifyDirectory(path);
        VerifyGenerationEntries(path);
        var owner = NativeTextOwnerFiles.ReadOwner(Path.Combine(path, NativeTextProtocol.OwnerFile), root, leaf,
            sourceNodeId, executionOptions: executionOptions);
        if (File.Exists(Path.Combine(path, NativeTextProtocol.OwnerPendingFile)))
        {
            throw NativeTextErrors.Corrupt();
        }
        if (File.Exists(Path.Combine(path, NativeTextProtocol.ManifestFile))
            || File.Exists(Path.Combine(path, NativeTextProtocol.PendingManifestFile)))
        {
            ValidateGeneration(path, root, leaf, sourceNodeId, limits, executionOptions: executionOptions);
        }
        else
        {
            NativeTextInventory.ValidateTrackedLayout(path, owner.OwnedPaths, allowMissingNative: true, executionOptions: executionOptions);
        }
        Directory.Delete(path, recursive: true);
    }

    internal static void ValidateGeneration(string path, string root, string leaf, Guid sourceNodeId, DatabaseLimits limits, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        NativeTextFileIO.VerifyDirectory(path);
        VerifyGenerationEntries(path);
        var owner = NativeTextOwnerFiles.ReadOwner(Path.Combine(path, NativeTextProtocol.OwnerFile), root, leaf,
            sourceNodeId, executionOptions: executionOptions);
        var manifestPath = Path.Combine(path, NativeTextProtocol.ManifestFile);
        var pendingPath = Path.Combine(path, NativeTextProtocol.PendingManifestFile);
        if (File.Exists(manifestPath) && File.Exists(pendingPath)
            || File.Exists(Path.Combine(path, NativeTextProtocol.OwnerPendingFile)))
        {
            throw NativeTextErrors.Corrupt();
        }
        var hasInventory = File.Exists(manifestPath) || File.Exists(pendingPath);
        NativeTextInventory.ValidateTrackedLayout(path, owner.OwnedPaths, allowMissingNative: !hasInventory, executionOptions: executionOptions);
        ValidateManifestIfPresent(manifestPath, owner, path, limits, executionOptions: executionOptions);
        ValidateManifestIfPresent(pendingPath, owner, path, limits, executionOptions: executionOptions);
        NativeTextFiles.CheckGenerationBound(path, executionOptions: executionOptions);
    }

    private static void ValidateManifestIfPresent(string manifestPath, NativeTextOwnerReceipt owner, string generationPath, DatabaseLimits limits, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        if (!File.Exists(manifestPath))
        {
            return;
        }
        var manifest = ReadManifest(manifestPath, owner.Scope!, limits, executionOptions: executionOptions);
        NativeTextInventory.Verify(generationPath, owner.OwnedPaths, manifest.Files, executionOptions: executionOptions);
    }

    internal static void VerifyGenerationEntries(string path)
    {
        const int EmptyAttributesFileAttributesReparsePoint = 0;
        const int EmptyAttributesFileAttributesDirectory = 0;

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
            if ((attributes & FileAttributes.ReparsePoint) != EmptyAttributesFileAttributesReparsePoint
                || (name == NativeTextProtocol.NativeDirectory) != ((attributes & FileAttributes.Directory) != EmptyAttributesFileAttributesDirectory))
            {
                throw NativeTextErrors.Ownership();
            }
        }
    }

    private static void EnsureGenerationCapacity(string root, Guid sourceNodeId, DatabaseLimits limits, NativeTextGenerationSlot? first, NativeTextGenerationSlot? second, NativeTextGenerationSlot? third, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        NativeTextRootFiles.VerifyReceipt(root, sourceNodeId, executionOptions: executionOptions);
        var count = 0;
        var files = 0;
        long bytes = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            if (Path.GetFileName(entry) == NativeTextProtocol.RootReceiptFile)
            {
                continue;
            }
            if (++count > executionOptions.Value.MaximumGenerations
                || !NativeTextValidation.IsGenerationLeaf(Path.GetFileName(entry))
                || !Directory.Exists(entry))
            {
                throw count > executionOptions.Value.MaximumGenerations
                    ? NativeTextErrors.BoundExceeded() : NativeTextErrors.Ownership();
            }
            var leaf = Path.GetFileName(entry);
            var live = NativeTextLiveGenerationPreflight.Find(root, entry, leaf, sourceNodeId,
                first, second, third);
            if (live is null)
            {
                ValidateGeneration(entry, root, leaf, sourceNodeId, limits, executionOptions: executionOptions);
            }
            else
            {
                NativeTextLiveGenerationPreflight.Validate(live, entry, root, leaf, sourceNodeId, limits, executionOptions: executionOptions);
            }
            var measured = NativeTextFileIO.MeasureRegularFiles(entry, executionOptions.Value.MaximumFiles,
                executionOptions.Value.MaximumDiskBytes, executionOptions: executionOptions);
            if (measured.Files > executionOptions.Value.MaximumFiles - files
                || measured.Bytes > executionOptions.Value.MaximumDiskBytes - bytes)
            {
                throw NativeTextErrors.BoundExceeded();
            }
            files += measured.Files;
            bytes += measured.Bytes;
        }
        if (count >= executionOptions.Value.MaximumGenerations
            || files >= executionOptions.Value.MaximumFiles
            || bytes > executionOptions.Value.MaximumDiskBytes - executionOptions.Value.MaximumOwnerReceiptBytes)
        {
            throw NativeTextErrors.BoundExceeded();
        }
    }
}
