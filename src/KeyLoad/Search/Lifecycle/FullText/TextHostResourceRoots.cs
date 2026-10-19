using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class TextHostResourceRoots
{
    internal const string BootstrapDirectory = "search-indexes";

    internal static TextResourceOwnership Register(string directory, Guid node,
        IOptions<TextExecutionOptions> options)
    {
        var owner = new TextResourceOwnership(options);
        foreach (var leaf in new[] { BootstrapDirectory, TextIncrementalProtocol.RootDirectory,
            TextOnlineRoot.DirectoryName })
        {
            var root = Path.GetFullPath(Path.Combine(directory, leaf));
            RuntimeTextIndex.ValidatePath(root);
            RuntimeTextPath.VerifyExistingAncestors(root);
            Directory.CreateDirectory(root);
            TextFileIO.VerifyDirectory(root);
            TextFileIO.SetPrivateDirectoryMode(root);
            var originalReceipt = File.Exists(Path.Combine(root, TextProtocol.RootReceiptFile));
            if (!originalReceipt && Directory.EnumerateFileSystemEntries(root).Any())
            {
                throw TextErrors.Ownership();
            }
            owner.RegisterRoot(root, budget =>
            {
                budget?.Check();
                TextFileIO.VerifyDirectory(root);
                if (originalReceipt)
                {
                    TextRootFiles.VerifyReceipt(root, node, options);
                }
            });
        }
        return owner;
    }
}
