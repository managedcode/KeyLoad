using KeyLoad.Storage.IO;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnPaths
{
    private const FileAttributes EmptyAttributes = (FileAttributes)0;
    private const int GuidDigits = 32;

    internal static string Generation(string root, string leaf)
    {
        if (!leaf.StartsWith(NativeAnnProtocol.GenerationPrefix, StringComparison.Ordinal)
            || leaf.Length != NativeAnnProtocol.GenerationPrefix.Length + GuidDigits
            || !Guid.TryParseExact(leaf.AsSpan(NativeAnnProtocol.GenerationPrefix.Length),
                NativeAnnProtocol.GuidFormat, out _))
        {
            throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt);
        }
        var path = Path.GetFullPath(Path.Combine(root, leaf));
        RequireDirectory(root);
        if (Path.GetDirectoryName(path) != Path.GetFullPath(root))
        {
            throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership);
        }
        if (Directory.Exists(path))
        { RequireDirectory(path); }
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
                throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership);
            }
        }
    }

    internal static string ExistingFile(string root, string leaf, string name)
    {
        if (name is not (NativeAnnProtocol.IndexFile or NativeAnnProtocol.ManifestFile))
        {
            throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership);
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
        var count = NativeAnnFileBounds.Initial;
        foreach (var file in Directory.EnumerateFileSystemEntries(directory))
        {
            ExistingFile(root, leaf, Path.GetFileName(file));
            count++;
        }
        if (count != NativeAnnFileBounds.FilesPerGeneration)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
    }
}
