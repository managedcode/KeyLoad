using Microsoft.Extensions.Options;
namespace KeyLoad.Server.Search;

internal static class TextOwnerFiles
{
    internal static void TrackPath(string root, string leaf, Guid sourceNodeId, string path, bool directory, IOptions<TextExecutionOptions> executionOptions, TextResourceOwnership resources)
        => TrackOwnedPaths(root, leaf, sourceNodeId,
            [new(RuntimeTextPath.Normalize(root, leaf, path), directory)], executionOptions, resources);

    internal static void TrackFiles(string root, string leaf, Guid sourceNodeId, string first, string? second,
        IOptions<TextExecutionOptions> executionOptions, TextResourceOwnership resources)
    {
        TextOwnedPath[] requested = second is null
            ? [new(RuntimeTextPath.Normalize(root, leaf, first), false)]
            : [new(RuntimeTextPath.Normalize(root, leaf, first), false), new(RuntimeTextPath.Normalize(root, leaf, second), false)];
        TrackOwnedPaths(root, leaf, sourceNodeId, requested, executionOptions, resources);
    }

    private static void TrackOwnedPaths(string root, string leaf, Guid sourceNodeId, TextOwnedPath[] requested,
        IOptions<TextExecutionOptions> executionOptions, TextResourceOwnership resources)
    {
        var generation = Path.Combine(root, leaf);
        var ownerPath = Path.Combine(generation, TextProtocol.OwnerFile);
        var owner = ReadOwner(ownerPath, root, leaf, sourceNodeId, executionOptions);
        foreach (var path in requested)
        {
            var existing = owner.OwnedPaths.FirstOrDefault(item => item.RelativePath == path.RelativePath);
            if (existing is not null && existing.IsDirectory != path.IsDirectory)
            {
                throw TextErrors.Ownership();
            }
        }
        if (requested.All(path => owner.OwnedPaths.Contains(path)))
        {
            return;
        }
        var paths = owner.OwnedPaths.Where(path => RetainPath(generation, path)).ToList();
        foreach (var path in requested)
        {
            if (!paths.Contains(path))
            {
                paths.Add(path);
            }
        }
        var ordered = paths.OrderBy(item => item.RelativePath, StringComparer.Ordinal).ToArray();
        TextValidation.ValidateOwnedPaths(ordered, executionOptions);
        var pending = Path.Combine(generation, TextProtocol.OwnerPendingFile);
        TextFileIO.WriteEnvelope(pending, owner with { OwnedPaths = ordered },
            executionOptions.Value.MaximumOwnerReceiptBytes, executionOptions, resources: resources);
        File.Move(pending, ownerPath, true);
    }

    private static bool RetainPath(string generation, TextOwnedPath path)
    {
        if (path.IsDirectory)
        {
            // The native directory creator admits its complete chain before creating it.
            return true;
        }
        var full = Path.Combine(generation, path.RelativePath);
        RuntimeTextPath.VerifyContainedAncestors(Path.Combine(generation, TextProtocol.RuntimeDirectoryCore), full);
        try
        {
            var attributes = File.GetAttributes(full);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw TextErrors.Ownership();
            }
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    internal static void RequirePath(string root, string leaf, Guid sourceNodeId, string path, bool directory, IOptions<TextExecutionOptions> executionOptions)
    {
        var owner = ReadOwner(Path.Combine(root, leaf, TextProtocol.OwnerFile), root, leaf, sourceNodeId, executionOptions: executionOptions);
        var relative = RuntimeTextPath.Normalize(root, leaf, path);
        if (!owner.OwnedPaths.Any(item => item.RelativePath == relative && item.IsDirectory == directory))
        {
            throw TextErrors.Ownership();
        }
    }

    internal static TextOwnerReceipt ReadOwner(string path, string root, string leaf, Guid sourceNodeId, IOptions<TextExecutionOptions> executionOptions)
    {
        TextFileIO.VerifyRegularFile(path);
        var owner = TextFileIO.ReadEnvelope<TextOwnerReceipt>(path, executionOptions.Value.MaximumOwnerReceiptBytes);
        if (owner is null || owner.OwnedPaths is null || owner.RootDirectory != root || owner.GenerationLeaf != leaf || owner.SourceNodeId != sourceNodeId
            || owner.Scope is null)
        {
            throw TextErrors.Ownership();
        }
        TextValidation.ValidateScope(owner.Scope, sourceNodeId);
        TextValidation.ValidateOwnedPaths(owner.OwnedPaths, executionOptions: executionOptions);
        return owner;
    }
}
