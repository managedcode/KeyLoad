using Microsoft.Extensions.Options;
namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOwnerFiles
{
    internal static void TrackPath(string root, string leaf, Guid sourceNodeId, string path, bool directory, IOptions<NativeTextExecutionOptions> executionOptions, NativeTextResourceOwnership? resources = null)
    {
        var generation = Path.Combine(root, leaf);
        var ownerPath = Path.Combine(generation, NativeTextProtocol.OwnerFile);
        var owner = ReadOwner(ownerPath, root, leaf, sourceNodeId, executionOptions: executionOptions);
        var relative = NativeTextPath.Normalize(root, leaf, path);
        var existing = owner.OwnedPaths.FirstOrDefault(item => item.RelativePath == relative);
        if (existing is not null)
        {
            if (existing.IsDirectory != directory)
            {
                throw NativeTextErrors.Ownership();
            }
            return;
        }
        if (owner.OwnedPaths.Length == executionOptions.Value.MaximumEntries)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        var paths = owner.OwnedPaths.Append(new NativeTextOwnedPath(relative, directory))
            .OrderBy(item => item.RelativePath, StringComparer.Ordinal).ToArray();
        NativeTextValidation.ValidateOwnedPaths(paths, executionOptions: executionOptions);
        var pending = Path.Combine(generation, NativeTextProtocol.OwnerPendingFile);
        NativeTextFileIO.WriteEnvelope(pending, owner with { OwnedPaths = paths }, executionOptions.Value.MaximumOwnerReceiptBytes, executionOptions: executionOptions, resources: resources);
        File.Move(pending, ownerPath, true);
    }

    internal static void RequirePath(string root, string leaf, Guid sourceNodeId, string path, bool directory, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        var owner = ReadOwner(Path.Combine(root, leaf, NativeTextProtocol.OwnerFile), root, leaf, sourceNodeId, executionOptions: executionOptions);
        var relative = NativeTextPath.Normalize(root, leaf, path);
        if (!owner.OwnedPaths.Any(item => item.RelativePath == relative && item.IsDirectory == directory))
        {
            throw NativeTextErrors.Ownership();
        }
    }

    internal static NativeTextOwnerReceipt ReadOwner(string path, string root, string leaf, Guid sourceNodeId, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        NativeTextFileIO.VerifyRegularFile(path);
        var owner = NativeTextFileIO.ReadEnvelope<NativeTextOwnerReceipt>(path, executionOptions.Value.MaximumOwnerReceiptBytes);
        if (owner is null || owner.OwnedPaths is null || owner.FormatVersion != NativeTextProtocol.FormatVersion
            || owner.RootDirectory != root || owner.GenerationLeaf != leaf || owner.SourceNodeId != sourceNodeId
            || owner.Scope is null)
        {
            throw NativeTextErrors.Ownership();
        }
        NativeTextValidation.ValidateScope(owner.Scope, sourceNodeId);
        NativeTextValidation.ValidateOwnedPaths(owner.OwnedPaths, executionOptions: executionOptions);
        return owner;
    }
}
