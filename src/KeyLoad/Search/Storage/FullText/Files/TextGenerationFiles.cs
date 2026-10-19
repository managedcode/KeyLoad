using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class TextGenerationFiles
{
    internal static void CreateOwner(string root, string leaf, Guid sourceNodeId, TextProjectionScope scope, DatabaseLimits limits, TextGenerationSlot? first, TextGenerationSlot? second, TextGenerationSlot? third, IOptions<TextExecutionOptions> executionOptions, TextResourceOwnership? resources = null)
    {
        EnsureGenerationCapacity(root, sourceNodeId, limits, first, second, third, executionOptions: executionOptions);
        var path = Path.Combine(root, leaf);
        if (Directory.Exists(path) || File.Exists(path))
        {
            throw TextErrors.Ownership();
        }
        if (resources is null)
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            resources.MutatePhysical(() => Directory.CreateDirectory(path));
        }
        TextFileIO.VerifyDirectory(path);
        TextFileIO.SetPrivateDirectoryMode(path);
        TextFileIO.WriteEnvelope(Path.Combine(path, TextProtocol.OwnerFile),
            new TextOwnerReceipt(TextProtocol.FormatVersion, root, leaf, sourceNodeId, scope,
                [new(TextProtocol.RuntimeDirectoryCore, true)]), executionOptions.Value.MaximumOwnerReceiptBytes, executionOptions: executionOptions, resources: resources);
    }

    internal static TextManifest ReadManifest(string path, TextProjectionScope scope, DatabaseLimits limits, IOptions<TextExecutionOptions> executionOptions)
    {
        var manifest = TextFileIO.ReadEnvelope<TextManifest>(path, limits.MaxQueryReadBytes);
        TextValidation.ValidateManifest(manifest, scope, limits.MaxScanRecords, executionOptions: executionOptions);
        return manifest;
    }

    internal static TextManifest ValidatePublished(string generationPath, string root, string leaf, Guid sourceNodeId, TextProjectionScope scope, DatabaseLimits limits, ReadExecutionBudget budget, IOptions<TextExecutionOptions> executionOptions)
    {
        budget.Check();
        TextFileIO.VerifyDirectory(generationPath);
        VerifyGenerationEntries(generationPath);
        var owner = TextOwnerFiles.ReadOwner(Path.Combine(generationPath, TextProtocol.OwnerFile),
            root, leaf, sourceNodeId, executionOptions: executionOptions);
        if (owner.Scope != scope || File.Exists(Path.Combine(generationPath, TextProtocol.OwnerPendingFile))
            || File.Exists(Path.Combine(generationPath, TextProtocol.PendingManifestFile)))
        {
            throw TextErrors.Corrupt();
        }
        TextInventory.ValidateTrackedLayout(generationPath, owner.OwnedPaths, budget: budget, executionOptions: executionOptions);
        var manifest = ReadManifest(Path.Combine(generationPath, TextProtocol.ManifestFile), scope, limits, executionOptions: executionOptions);
        TextInventory.Verify(generationPath, owner.OwnedPaths, manifest.Files, budget: budget, executionOptions: executionOptions);
        TextFiles.CheckGenerationBound(generationPath, budget: budget, executionOptions: executionOptions);
        budget.Check();
        return manifest;
    }

    internal static void DeleteOwned(string root, string leaf, Guid sourceNodeId, DatabaseLimits limits, IOptions<TextExecutionOptions> executionOptions, TextResourceOwnership? resources = null)
    {
        var path = Path.Combine(root, leaf);
        if (!Directory.Exists(path))
        {
            return;
        }
        ValidateGeneration(path, root, leaf, sourceNodeId, limits, executionOptions: executionOptions);
        if (resources is null)
        {
            Directory.Delete(path, recursive: true);
        }
        else
        {
            resources.DeleteOwnedDirectory(path, () => Directory.Delete(path, recursive: true));
        }
    }

    internal static void DeleteBuilding(string root, string leaf, Guid sourceNodeId, DatabaseLimits limits, IOptions<TextExecutionOptions> executionOptions, TextResourceOwnership? resources = null)
    {
        var path = Path.Combine(root, leaf);
        if (!Directory.Exists(path))
        {
            return;
        }
        TextFileIO.VerifyDirectory(path);
        VerifyGenerationEntries(path);
        var owner = TextOwnerFiles.ReadOwner(Path.Combine(path, TextProtocol.OwnerFile), root, leaf,
            sourceNodeId, executionOptions: executionOptions);
        if (File.Exists(Path.Combine(path, TextProtocol.OwnerPendingFile)))
        {
            throw TextErrors.Corrupt();
        }
        if (File.Exists(Path.Combine(path, TextProtocol.ManifestFile))
            || File.Exists(Path.Combine(path, TextProtocol.PendingManifestFile)))
        {
            ValidateGeneration(path, root, leaf, sourceNodeId, limits, executionOptions: executionOptions);
        }
        else
        {
            TextInventory.ValidateTrackedLayout(path, owner.OwnedPaths, runtimeAllowMissing: true, executionOptions: executionOptions);
        }
        if (resources is null)
        {
            Directory.Delete(path, recursive: true);
        }
        else
        {
            resources.DeleteOwnedDirectory(path, () => Directory.Delete(path, recursive: true));
        }
    }

    internal static void ValidateGeneration(string path, string root, string leaf, Guid sourceNodeId, DatabaseLimits limits, IOptions<TextExecutionOptions> executionOptions)
    {
        TextFileIO.VerifyDirectory(path);
        VerifyGenerationEntries(path);
        var owner = TextOwnerFiles.ReadOwner(Path.Combine(path, TextProtocol.OwnerFile), root, leaf,
            sourceNodeId, executionOptions: executionOptions);
        var manifestPath = Path.Combine(path, TextProtocol.ManifestFile);
        var pendingPath = Path.Combine(path, TextProtocol.PendingManifestFile);
        if (File.Exists(manifestPath) && File.Exists(pendingPath)
            || File.Exists(Path.Combine(path, TextProtocol.OwnerPendingFile)))
        {
            throw TextErrors.Corrupt();
        }
        var hasInventory = File.Exists(manifestPath) || File.Exists(pendingPath);
        TextInventory.ValidateTrackedLayout(path, owner.OwnedPaths, runtimeAllowMissing: !hasInventory, executionOptions: executionOptions);
        ValidateManifestIfPresent(manifestPath, owner, path, limits, executionOptions: executionOptions);
        ValidateManifestIfPresent(pendingPath, owner, path, limits, executionOptions: executionOptions);
        TextFiles.CheckGenerationBound(path, executionOptions: executionOptions);
    }

    private static void ValidateManifestIfPresent(string manifestPath, TextOwnerReceipt owner, string generationPath, DatabaseLimits limits, IOptions<TextExecutionOptions> executionOptions)
    {
        if (!File.Exists(manifestPath))
        {
            return;
        }
        var manifest = ReadManifest(manifestPath, owner.Scope!, limits, executionOptions: executionOptions);
        TextInventory.Verify(generationPath, owner.OwnedPaths, manifest.Files, executionOptions: executionOptions);
    }

    internal static void VerifyGenerationEntries(string path)
    {
        const int EmptyAttributesFileAttributesReparsePoint = 0;
        const int EmptyAttributesFileAttributesDirectory = 0;

        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var name = Path.GetFileName(entry);
            if (name is not (TextProtocol.OwnerFile or TextProtocol.ManifestFile
                or TextProtocol.PendingManifestFile or TextProtocol.OwnerPendingFile
                or TextProtocol.RuntimeDirectoryCore))
            {
                throw TextErrors.Ownership();
            }
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != EmptyAttributesFileAttributesReparsePoint
                || (name == TextProtocol.RuntimeDirectoryCore) != ((attributes & FileAttributes.Directory) != EmptyAttributesFileAttributesDirectory))
            {
                throw TextErrors.Ownership();
            }
        }
    }

    private static void EnsureGenerationCapacity(string root, Guid sourceNodeId, DatabaseLimits limits, TextGenerationSlot? first, TextGenerationSlot? second, TextGenerationSlot? third, IOptions<TextExecutionOptions> executionOptions)
    {
        TextRootFiles.VerifyReceipt(root, sourceNodeId, executionOptions: executionOptions);
        const int EmptyInventory = 0;
        var count = EmptyInventory;
        var files = EmptyInventory;
        long bytes = EmptyInventory;
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            if (Path.GetFileName(entry) == TextProtocol.RootReceiptFile)
            {
                continue;
            }
            if (++count > executionOptions.Value.MaximumGenerations
                || !TextValidation.IsGenerationLeaf(Path.GetFileName(entry))
                || !Directory.Exists(entry))
            {
                throw count > executionOptions.Value.MaximumGenerations
                    ? TextErrors.BoundExceeded() : TextErrors.Ownership();
            }
            var leaf = Path.GetFileName(entry);
            var live = TextLiveGenerationPreflight.Find(root, entry, leaf, sourceNodeId,
                first, second, third);
            if (live is null)
            {
                ValidateGeneration(entry, root, leaf, sourceNodeId, limits, executionOptions: executionOptions);
            }
            else
            {
                TextLiveGenerationPreflight.Validate(live, entry, root, leaf, sourceNodeId, limits, executionOptions: executionOptions);
            }
            var measured = TextFileIO.MeasureRegularFiles(entry, executionOptions.Value.MaximumFiles,
                executionOptions.Value.MaximumDiskBytes, executionOptions: executionOptions);
            if (measured.Files > executionOptions.Value.MaximumFiles - files
                || measured.Bytes > executionOptions.Value.MaximumDiskBytes - bytes)
            {
                throw TextErrors.BoundExceeded();
            }
            files += measured.Files;
            bytes += measured.Bytes;
        }
        if (count >= executionOptions.Value.MaximumGenerations
            || files >= executionOptions.Value.MaximumFiles
            || bytes > executionOptions.Value.MaximumDiskBytes - executionOptions.Value.MaximumOwnerReceiptBytes)
        {
            throw TextErrors.BoundExceeded();
        }
    }
}
