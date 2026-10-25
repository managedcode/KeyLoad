using KeyLoad.Storage.ZoneTree.IO;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class TextLiveGenerationMetadata
{
    internal static void Validate(TextGeneration generation, string path, string root, string leaf, Guid sourceNodeId, IOptions<TextExecutionOptions> executionOptions)
    {
        if (!generation.Published || generation.CurrentIndex is null
            || !StringComparer.Ordinal.Equals(generation.Path, path)
            || !StringComparer.Ordinal.Equals(generation.Path, Path.Combine(root, leaf))
            || !StringComparer.Ordinal.Equals(generation.Leaf, leaf)
            || generation.Scope.NodeId != sourceNodeId)
        {
            throw TextErrors.Ownership();
        }
        TextGenerationFiles.VerifyGenerationEntries(path);
        var ownerPath = Path.Combine(path, TextProtocol.OwnerFile);
        var manifestPath = Path.Combine(path, TextProtocol.ManifestFile);
        if (File.Exists(Path.Combine(path, TextProtocol.OwnerPendingFile))
            || File.Exists(Path.Combine(path, TextProtocol.PendingManifestFile))
            || !File.Exists(manifestPath))
        {
            throw TextErrors.Corrupt();
        }
        var owner = TextOwnerFiles.ReadOwner(ownerPath, root, leaf, sourceNodeId, executionOptions: executionOptions);
        if (owner.Scope != generation.Scope)
        {
            throw TextErrors.Ownership();
        }
        var manifest = TextGenerationFiles.ReadManifest(manifestPath, generation.Scope, executionOptions: executionOptions);
        if (!TextLiveManifest.Matches(generation, manifest))
        {
            throw TextErrors.Corrupt();
        }
        var expectedPaths = TextLiveOwnedPaths.FromFiles(manifest.Files, executionOptions: executionOptions);
        RequireManifestPathsTracked(expectedPaths, owner.OwnedPaths);
        TextLiveOwnedPaths.VerifyFilesystem(path, owner.OwnedPaths, expectedPaths, executionOptions: executionOptions);
        VerifyCurrentSizes(path, manifest.Files, executionOptions: executionOptions);
    }

    private static void RequireManifestPathsTracked(TextOwnedPath[] expectedPaths,
        TextOwnedPath[] ownedPaths)
    {
        foreach (var expected in expectedPaths)
        {
            if (!ownedPaths.Contains(expected))
            {
                throw TextErrors.Ownership();
            }
        }
    }

    private static void VerifyCurrentSizes(string path, TextFile[] files, IOptions<TextExecutionOptions> executionOptions)
    {
        const char SlashCharacter = '/';

        foreach (var file in files)
        {
            var filePath = Path.Combine(path, file.RelativePath.Replace(SlashCharacter, Path.DirectorySeparatorChar));
            if (OfflineRegularFile.Inspect(filePath).Length > executionOptions.Value.MaximumDiskBytes)
            {
                throw TextErrors.BoundExceeded();
            }
        }
    }
}
