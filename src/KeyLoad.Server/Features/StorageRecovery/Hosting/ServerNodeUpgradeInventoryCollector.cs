using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class ServerNodeUpgradeInventoryCollector(string root,
    IReadOnlyDictionary<string, FileStream>? held, bool excludePreparedReceipt, bool excludeProgressReceipt,
    IOptions<ServerNodeUpgradeExecutionOptions> executionOptions)
{
    private readonly ServerNodeUpgradeExecutionOptions settings = executionOptions.Value;
    private readonly List<ServerNodeUpgradeEntry> entries = [];
    private int files;
    private int directories;
    private long bytes;
    private long characters;

    internal IReadOnlyList<ServerNodeUpgradeEntry> Collect()
    {
        const int CollectEmptyCount = 0;

        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, CollectEmptyCount));
        while (pending.TryPop(out var next))
        { CollectDirectory(next.Path, next.Depth, pending); }
        entries.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        return entries.AsReadOnly();
    }

    private void CollectDirectory(string directory, int depth, Stack<(string Path, int Depth)> pending)
    {
        const char SlashCharacter = '/';
        const int EmptyAttributesFileAttributesReparsePoint = 0;
        const int EmptyAttributesFileAttributesDirectory = 0;
        const int LengthEmptyCount = 0;
        const int DepthStep = 1;

        if (depth > settings.MaximumDepth)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ServerNodeUpgradeProtocol.Limit); }
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, SlashCharacter);
            if (excludePreparedReceipt && relative == ServerNodeUpgradeProtocol.PreparedReceipt
                || excludeProgressReceipt && relative == ServerNodeUpgradeProtocol.ProgressReceipt)
            { continue; }
            ServerNodeUpgradePaths.CheckAncestors(path, false);
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != EmptyAttributesFileAttributesReparsePoint)
            { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
            var isDirectory = (attributes & FileAttributes.Directory) != EmptyAttributesFileAttributesDirectory;
            AccountPath(relative, isDirectory);
            if (isDirectory)
            {
                entries.Add(new(relative, true, LengthEmptyCount, Convert.ToHexStringLower(SHA256.HashData([]))));
                pending.Push((path, depth + DepthStep));
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
        if (entries.Count >= settings.MaximumEntries
            || files > settings.MaximumFiles || directories > settings.MaximumDirectories
            || relative.Length > ServerNodeUpgradeProtocol.MaximumPathCharacters
            || characters > settings.MaximumTotalPathCharacters)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ServerNodeUpgradeProtocol.Limit); }
    }

    private ServerNodeUpgradeEntry ReadFile(string path, string relative)
    {
        if (held is not null && held.TryGetValue(relative, out var ownership))
        { return ReadStream(ownership, relative); }
        using var input = ServerNodeUpgradeFiles.OpenRead(path, executionOptions: executionOptions);
        return ReadStream(input, relative);
    }

    private ServerNodeUpgradeEntry ReadStream(FileStream input, string relative)
    {
        const int PositionEmptyCount = 0;

        var length = input.Length;
        if (length > settings.MaximumSourceBytes - bytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ServerNodeUpgradeProtocol.Limit); }
        bytes += length;
        input.Position = PositionEmptyCount;
        var sha = Convert.ToHexStringLower(SHA256.HashData(input));
        if (length != input.Length || input.Position != length)
        { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
        return new(relative, false, length, sha);
    }
}
