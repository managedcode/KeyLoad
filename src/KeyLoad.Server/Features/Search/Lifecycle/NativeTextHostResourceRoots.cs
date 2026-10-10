using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextHostResourceRoots
{
    internal const string BootstrapDirectory = "search-indexes";

    internal static NativeTextResourceOwnership Register(string directory, Guid node,
        IOptions<NativeTextExecutionOptions> options)
    {
        var owner = new NativeTextResourceOwnership(options);
        foreach (var leaf in new[] { BootstrapDirectory, NativeTextIncrementalProtocol.RootDirectory,
            NativeTextOnlineRoot.DirectoryName })
        {
            var root = Path.GetFullPath(Path.Combine(directory, leaf));
            NativeTextIndex.ValidatePath(root);
            NativeTextPath.VerifyExistingAncestors(root);
            Directory.CreateDirectory(root);
            NativeTextFileIO.VerifyDirectory(root);
            NativeTextFileIO.SetPrivateDirectoryMode(root);
            var originalReceipt = File.Exists(Path.Combine(root, NativeTextProtocol.RootReceiptFile));
            if (!originalReceipt && Directory.EnumerateFileSystemEntries(root).Any())
            { throw NativeTextErrors.Ownership(); }
            owner.RegisterRoot(root, budget =>
            {
                budget?.Check();
                NativeTextFileIO.VerifyDirectory(root);
                if (originalReceipt)
                { NativeTextRootFiles.VerifyReceipt(root, node, options); }
            });
        }
        return owner;
    }
}
