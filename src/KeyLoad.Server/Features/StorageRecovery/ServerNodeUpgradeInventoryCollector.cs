using System.Security.Cryptography;

namespace KeyLoad.Server;

internal sealed class ServerNodeUpgradeInventoryCollector(string root,
    IReadOnlyDictionary<string, FileStream>? held, bool excludePreparedReceipt, bool excludeProgressReceipt)
{
    private readonly List<ServerNodeUpgradeEntry> entries = [];
    private int files;
    private int directories;
    private long bytes;
    private long characters;

    internal IReadOnlyList<ServerNodeUpgradeEntry> Collect()
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        while (pending.TryPop(out var next))
        { CollectDirectory(next.Path, next.Depth, pending); }
        entries.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        return entries.AsReadOnly();
    }

    private void CollectDirectory(string directory, int depth, Stack<(string Path, int Depth)> pending)
    {
        if (depth > ServerNodeUpgradeProtocol.MaximumDepth)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ServerNodeUpgradeProtocol.Limit); }
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            if (excludePreparedReceipt && relative == ServerNodeUpgradeProtocol.PreparedReceipt
                || excludeProgressReceipt && relative == ServerNodeUpgradeProtocol.ProgressReceipt)
            { continue; }
            ServerNodeUpgradePaths.CheckAncestors(path, false);
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
            var isDirectory = (attributes & FileAttributes.Directory) != 0;
            AccountPath(relative, isDirectory);
            if (isDirectory)
            {
                entries.Add(new(relative, true, 0, Convert.ToHexStringLower(SHA256.HashData([]))));
                pending.Push((path, depth + 1));
            }
            else
            { entries.Add(ReadFile(path, relative)); }
        }
    }

    private void AccountPath(string relative, bool directory)
    {
        characters += relative.Length;
        if (directory)
        { directories++; }
        else
        { files++; }
        if (entries.Count >= ServerNodeUpgradeProtocol.MaximumEntries
            || files > ServerNodeUpgradeProtocol.MaximumFiles || directories > ServerNodeUpgradeProtocol.MaximumDirectories
            || relative.Length > ServerNodeUpgradeProtocol.MaximumPathCharacters
            || characters > ServerNodeUpgradeProtocol.MaximumTotalPathCharacters)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ServerNodeUpgradeProtocol.Limit); }
    }

    private ServerNodeUpgradeEntry ReadFile(string path, string relative)
    {
        if (held is not null && held.TryGetValue(relative, out var ownership))
        { return ReadStream(ownership, relative); }
        using var input = ServerNodeUpgradeFiles.OpenRead(path);
        return ReadStream(input, relative);
    }

    private ServerNodeUpgradeEntry ReadStream(FileStream input, string relative)
    {
        var length = input.Length;
        if (length > ServerNodeUpgradeProtocol.MaximumSourceBytes - bytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ServerNodeUpgradeProtocol.Limit); }
        bytes += length;
        input.Position = 0;
        var sha = Convert.ToHexStringLower(SHA256.HashData(input));
        if (length != input.Length || input.Position != length)
        { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
        return new(relative, false, length, sha);
    }
}
