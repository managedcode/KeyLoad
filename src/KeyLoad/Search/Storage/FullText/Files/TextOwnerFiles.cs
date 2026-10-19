using Microsoft.Extensions.Options;
namespace KeyLoad.Server.Features.Search;

internal static class TextOwnerFiles
{
    internal static void TrackPath(string root, string leaf, Guid sourceNodeId, string path, bool directory, IOptions<TextExecutionOptions> executionOptions, TextResourceOwnership? resources = null)
    {
        var generation = Path.Combine(root, leaf);
        var ownerPath = Path.Combine(generation, TextProtocol.OwnerFile);
        var owner = ReadOwner(ownerPath, root, leaf, sourceNodeId, executionOptions: executionOptions);
        var relative = RuntimeTextPath.Normalize(root, leaf, path);
        var existing = owner.OwnedPaths.FirstOrDefault(item => item.RelativePath == relative);
        if (existing is not null)
        {
            if (existing.IsDirectory != directory)
            {
                throw TextErrors.Ownership();
            }
            return;
        }
        if (owner.OwnedPaths.Length == executionOptions.Value.MaximumEntries)
        {
            throw TextErrors.BoundExceeded();
        }
        var paths = owner.OwnedPaths.Append(new TextOwnedPath(relative, directory))
            .OrderBy(item => item.RelativePath, StringComparer.Ordinal).ToArray();
        TextValidation.ValidateOwnedPaths(paths, executionOptions: executionOptions);
        var pending = Path.Combine(generation, TextProtocol.OwnerPendingFile);
        TextFileIO.WriteEnvelope(pending, owner with
        {
            OwnedPaths = paths
        }, executionOptions.Value.MaximumOwnerReceiptBytes, executionOptions: executionOptions, resources: resources);
        File.Move(pending, ownerPath, true);
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
        if (owner is null || owner.OwnedPaths is null || owner.FormatVersion != TextProtocol.FormatVersion
            || owner.RootDirectory != root || owner.GenerationLeaf != leaf || owner.SourceNodeId != sourceNodeId
            || owner.Scope is null)
        {
            throw TextErrors.Ownership();
        }
        TextValidation.ValidateScope(owner.Scope, sourceNodeId);
        TextValidation.ValidateOwnedPaths(owner.OwnedPaths, executionOptions: executionOptions);
        return owner;
    }
}
