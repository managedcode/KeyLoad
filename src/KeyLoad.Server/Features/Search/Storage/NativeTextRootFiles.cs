namespace KeyLoad.Server.Features.Search;

internal static class NativeTextRootFiles
{
    internal static void InitializeReceipt(string root, Guid sourceNodeId)
    {
        var path = Path.Combine(root, NativeTextProtocol.RootReceiptFile);
        if (File.Exists(path))
        {
            VerifyReceipt(root, sourceNodeId);
            return;
        }
        if (Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw NativeTextErrors.Ownership();
        }
        NativeTextFileIO.WriteEnvelope(path, new NativeTextOwnerReceipt(NativeTextProtocol.FormatVersion,
            root, string.Empty, sourceNodeId, null, []), 65_536);
    }

    internal static void RetireRestartGenerations(string root, Guid sourceNodeId, DatabaseLimits limits)
    {
        var leaves = new List<string>(2);
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            if (Path.GetFileName(entry) == NativeTextProtocol.RootReceiptFile)
            {
                continue;
            }
            if (leaves.Count == 2)
            {
                throw NativeTextErrors.BoundExceeded();
            }
            var leaf = Path.GetFileName(entry);
            if (!NativeTextValidation.IsGenerationLeaf(leaf) || !Directory.Exists(entry))
            {
                throw NativeTextErrors.Ownership();
            }
            leaves.Add(leaf);
            NativeTextGenerationFiles.ValidateGeneration(entry, root, leaf, sourceNodeId, limits);
        }
        foreach (var leaf in leaves)
        {
            Directory.Delete(Path.Combine(root, leaf), recursive: true);
        }
    }

    internal static void VerifyReceipt(string root, Guid sourceNodeId)
    {
        var path = Path.Combine(root, NativeTextProtocol.RootReceiptFile);
        NativeTextFileIO.VerifyRegularFile(path);
        var owner = NativeTextFileIO.ReadEnvelope<NativeTextOwnerReceipt>(path, 65_536);
        if (owner is null || owner.FormatVersion != NativeTextProtocol.FormatVersion || owner.RootDirectory != root
            || !string.IsNullOrEmpty(owner.GenerationLeaf) || owner.SourceNodeId != sourceNodeId
            || owner.Scope is not null || owner.OwnedPaths is null || owner.OwnedPaths.Length != 0)
        {
            throw NativeTextErrors.Ownership();
        }
    }
}
