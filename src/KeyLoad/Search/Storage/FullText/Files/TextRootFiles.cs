using Microsoft.Extensions.Options;
namespace KeyLoad.Server.Features.Search;

internal static class TextRootFiles
{
    internal static void InitializeReceipt(string root, Guid sourceNodeId, IOptions<TextExecutionOptions> executionOptions, TextResourceOwnership? resources = null)
    {
        var path = Path.Combine(root, TextProtocol.RootReceiptFile);
        if (File.Exists(path))
        {
            VerifyReceipt(root, sourceNodeId, executionOptions: executionOptions);
            return;
        }
        if (Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw TextErrors.Ownership();
        }
        TextFileIO.WriteEnvelope(path, new TextOwnerReceipt(TextProtocol.FormatVersion,
            root, string.Empty, sourceNodeId, null, []), executionOptions.Value.MaximumOwnerReceiptBytes, executionOptions: executionOptions, resources: resources);
    }

    internal static void RetireRestartGenerations(string root, Guid sourceNodeId, DatabaseLimits limits, IOptions<TextExecutionOptions> executionOptions, TextResourceOwnership? resources = null)
    {
        const int FilesInitialValue = 0;
        const int BytesInitialValue = 0;

        var leaves = new List<string>(executionOptions.Value.MaximumGenerations);
        var files = FilesInitialValue;
        long bytes = BytesInitialValue;
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            if (Path.GetFileName(entry) == TextProtocol.RootReceiptFile)
            {
                continue;
            }
            if (leaves.Count == executionOptions.Value.MaximumGenerations)
            {
                throw TextErrors.BoundExceeded();
            }
            var leaf = Path.GetFileName(entry);
            if (!TextValidation.IsGenerationLeaf(leaf) || !Directory.Exists(entry))
            {
                throw TextErrors.Ownership();
            }
            leaves.Add(leaf);
            TextGenerationFiles.ValidateGeneration(entry, root, leaf, sourceNodeId, limits, executionOptions: executionOptions);
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
        foreach (var leaf in leaves)
        {
            var path = Path.Combine(root, leaf);
            if (resources is null)
            {
                Directory.Delete(path, recursive: true);
            }
            else
            {
                resources.DeleteOwnedDirectory(path, () => Directory.Delete(path, recursive: true));
            }
        }
    }

    internal static void VerifyReceipt(string root, Guid sourceNodeId, IOptions<TextExecutionOptions> executionOptions)
    {
        const int EmptyOwnedPathsLength = 0;

        var path = Path.Combine(root, TextProtocol.RootReceiptFile);
        TextFileIO.VerifyRegularFile(path);
        var owner = TextFileIO.ReadEnvelope<TextOwnerReceipt>(path, executionOptions.Value.MaximumOwnerReceiptBytes);
        if (owner is null || owner.FormatVersion != TextProtocol.FormatVersion || owner.RootDirectory != root
            || !string.IsNullOrEmpty(owner.GenerationLeaf) || owner.SourceNodeId != sourceNodeId
            || owner.Scope is not null || owner.OwnedPaths is null || owner.OwnedPaths.Length != EmptyOwnedPathsLength)
        {
            throw TextErrors.Ownership();
        }
    }
}
