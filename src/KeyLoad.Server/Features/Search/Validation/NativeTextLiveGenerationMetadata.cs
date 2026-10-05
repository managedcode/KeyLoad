using KeyLoad.Storage.IO;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextLiveGenerationMetadata
{
    internal static void Validate(NativeTextGeneration generation, string path, string root, string leaf,
        Guid sourceNodeId, DatabaseLimits limits)
    {
        if (!generation.Published || generation.CurrentIndex is null
            || !StringComparer.Ordinal.Equals(generation.Path, path)
            || !StringComparer.Ordinal.Equals(generation.Path, Path.Combine(root, leaf))
            || !StringComparer.Ordinal.Equals(generation.Leaf, leaf)
            || generation.Scope.NodeId != sourceNodeId)
        {
            throw NativeTextErrors.Ownership();
        }
        NativeTextGenerationFiles.VerifyGenerationEntries(path);
        var ownerPath = Path.Combine(path, NativeTextProtocol.OwnerFile);
        var manifestPath = Path.Combine(path, NativeTextProtocol.ManifestFile);
        if (File.Exists(Path.Combine(path, NativeTextProtocol.OwnerPendingFile))
            || File.Exists(Path.Combine(path, NativeTextProtocol.PendingManifestFile))
            || !File.Exists(manifestPath))
        {
            throw NativeTextErrors.Corrupt();
        }
        var owner = NativeTextOwnerFiles.ReadOwner(ownerPath, root, leaf, sourceNodeId);
        if (owner.Scope != generation.Scope)
        {
            throw NativeTextErrors.Ownership();
        }
        var manifest = NativeTextGenerationFiles.ReadManifest(manifestPath, generation.Scope, limits);
        if (!NativeTextLiveManifest.Matches(generation, manifest))
        {
            throw NativeTextErrors.Corrupt();
        }
        var expectedPaths = NativeTextLiveOwnedPaths.FromFiles(manifest.Files);
        RequireManifestPathsTracked(expectedPaths, owner.OwnedPaths);
        NativeTextLiveOwnedPaths.VerifyFilesystem(path, owner.OwnedPaths, expectedPaths);
        VerifyCurrentSizes(path, manifest.Files);
    }

    private static void RequireManifestPathsTracked(NativeTextOwnedPath[] expectedPaths,
        NativeTextOwnedPath[] ownedPaths)
    {
        foreach (var expected in expectedPaths)
        {
            if (!ownedPaths.Contains(expected))
            {
                throw NativeTextErrors.Ownership();
            }
        }
    }

    private static void VerifyCurrentSizes(string path, NativeTextFile[] files)
    {
        foreach (var file in files)
        {
            var filePath = Path.Combine(path, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (OfflineRegularFile.Inspect(filePath).Length > NativeTextProtocol.MaximumDiskBytes)
            {
                throw NativeTextErrors.BoundExceeded();
            }
        }
    }
}
