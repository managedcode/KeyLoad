using KeyLoad.Storage.ZoneTree.IO;

namespace KeyLoad.Server.Features.Search;

internal static class AnnPaths
{
    private const FileAttributes EmptyAttributes = (FileAttributes)0;
    private const int GuidDigits = 32;

    internal static string Generation(string root, string leaf)
    {
        if (!leaf.StartsWith(AnnProtocol.GenerationPrefix, StringComparison.Ordinal)
            || leaf.Length != AnnProtocol.GenerationPrefix.Length + GuidDigits
            || !Guid.TryParseExact(leaf.AsSpan(AnnProtocol.GenerationPrefix.Length),
                AnnProtocol.GuidFormat, out _))
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt);
        }
        var path = Path.GetFullPath(Path.Combine(root, leaf));
        RequireDirectory(root);
        if (Path.GetDirectoryName(path) != Path.GetFullPath(root))
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
        }
        if (Directory.Exists(path))
        {
            RequireDirectory(path);
        }
        return path;
    }

    internal static void RequireDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        for (var current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != EmptyAttributes
                || (attributes & FileAttributes.Directory) == EmptyAttributes)
            {
                throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
            }
        }
    }

    internal static string ExistingFile(string root, string leaf, string name)
    {
        if (name is not (AnnProtocol.IndexFile or AnnProtocol.ManifestFile))
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
        }
        var directory = Generation(root, leaf);
        RequireDirectory(directory);
        var file = Path.Combine(directory, name);
        OfflineRegularFile.RequireRegular(file);
        return file;
    }

    internal static void RequireClosedGeneration(string root, string leaf)
    {
        var directory = Generation(root, leaf);
        var count = AnnFileBounds.Initial;
        foreach (var file in Directory.EnumerateFileSystemEntries(directory))
        {
            ExistingFile(root, leaf, Path.GetFileName(file));
            count++;
        }
        if (count != AnnFileBounds.FilesPerGeneration)
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt);
        }
    }
}
