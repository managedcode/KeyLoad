using System.Collections.Immutable;
using System.Security.Cryptography;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal sealed record NodeEpochRf3FileEntry(string Path, long Length, string Sha256, int Mode);

internal sealed record NodeEpochRf3Inventory(ImmutableArray<NodeEpochRf3FileEntry> Files,
    ImmutableArray<string> Directories, long TotalBytes)
{
    private const string UnknownMarker = "owned-unknown-node-entry-v1";
    internal static async Task<NodeEpochRf3Inventory> CaptureAsync(string root, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var fullRoot = Path.GetFullPath(root);
        RequireDirectory(fullRoot);
        var files = ImmutableArray.CreateBuilder<NodeEpochRf3FileEntry>();
        var directories = ImmutableArray.CreateBuilder<string>();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((fullRoot, 0));
        long totalBytes = 0;
        var totalPathCharacters = 0;
        while (pending.TryPop(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current.Path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var totals = await CaptureEntryAsync(fullRoot, entry, current.Depth, pending, files, directories,
                    totalBytes, totalPathCharacters, cancellationToken).ConfigureAwait(false);
                totalBytes = totals.TotalBytes;
                totalPathCharacters = totals.TotalPathCharacters;
            }
        }
        var sortedFiles = files.OrderBy(file => file.Path, StringComparer.Ordinal).ToImmutableArray();
        var sortedDirectories = directories.Order(StringComparer.Ordinal).ToImmutableArray();
        return new(sortedFiles, sortedDirectories, totalBytes);
    }

    private static async Task<(long TotalBytes, int TotalPathCharacters)> CaptureEntryAsync(string root,
        string entry, int parentDepth, Stack<(string Path, int Depth)> pending,
        ImmutableArray<NodeEpochRf3FileEntry>.Builder files, ImmutableArray<string>.Builder directories,
        long totalBytes, int totalPathCharacters, CancellationToken cancellationToken)
    {
        var attributes = File.GetAttributes(entry);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        { throw new InvalidDataException("Node inventory contains a link or reparse point."); }
        var relative = Path.GetRelativePath(root, entry);
        ValidateRelativePath(relative, totalPathCharacters);
        totalPathCharacters += relative.Length;
        if ((attributes & FileAttributes.Directory) != 0)
        {
            AddDirectoryPath(entry, relative, parentDepth, pending, directories);
            return (totalBytes, totalPathCharacters);
        }
        var file = new FileInfo(entry);
        if (files.Count >= NodeEpochRf3Protocol.MaximumFiles
            || file.Length < 0 || file.Length > NodeEpochRf3Protocol.MaximumInventoryBytes - totalBytes)
        { throw new InvalidDataException("Node inventory exceeds its file or byte bound."); }
        var digest = await HashStableAsync(entry, file.Length, cancellationToken).ConfigureAwait(false);
        files.Add(new(relative, file.Length, digest, ReadMode(entry)));
        return (totalBytes + file.Length, totalPathCharacters);
    }

    private static void ValidateRelativePath(string relative, int totalPathCharacters)
    {
        if (relative.Length is < 1 or > NodeEpochRf3Protocol.MaximumPathCharacters || Path.IsPathRooted(relative))
        { throw new InvalidDataException("Node inventory contains an invalid relative path."); }
        if (relative.Length > NodeEpochRf3Protocol.MaximumTotalPathCharacters - totalPathCharacters)
        { throw new InvalidDataException("Node inventory exceeds its aggregate path bound."); }
    }

    private static void AddDirectoryPath(string entry, string relative, int parentDepth,
        Stack<(string Path, int Depth)> pending, ImmutableArray<string>.Builder directories)
    {
        if (parentDepth >= NodeEpochRf3Protocol.MaximumDepth
            || directories.Count >= NodeEpochRf3Protocol.MaximumDirectories)
        { throw new InvalidDataException("Node inventory exceeds its directory or depth bound."); }
        directories.Add(relative);
        pending.Push((entry, parentDepth + 1));
    }

    internal static async Task<NodeEpochRf3Inventory> CopyTreeAsync(string source, string destination,
        CancellationToken cancellationToken)
    {
        var inventory = await CaptureAsync(source, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(destination);
        MakePrivateDirectory(destination);
        foreach (var relative in inventory.Directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Resolve(destination, relative);
            Directory.CreateDirectory(directory);
            MakePrivateDirectory(directory);
        }
        foreach (var file in inventory.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CopyFileAsync(Resolve(source, file.Path), Resolve(destination, file.Path), file, cancellationToken)
                .ConfigureAwait(false);
        }
        var copied = await CaptureAsync(destination, cancellationToken).ConfigureAwait(false);
        if (!inventory.Equivalent(copied))
        { throw new IOException("The private node copy does not match its source inventory."); }
        return inventory;
    }

    internal bool Equivalent(NodeEpochRf3Inventory other) => TotalBytes == other.TotalBytes
        && Files.SequenceEqual(other.Files) && Directories.SequenceEqual(other.Directories, StringComparer.Ordinal);

    internal static async Task WriteOwnedUnknownAsync(string directory, CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, NodeEpochRf3Protocol.UnknownEntry);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            NodeEpochRf3Protocol.BufferBytes, FileOptions.Asynchronous | FileOptions.WriteThrough);
        var bytes = System.Text.Encoding.UTF8.GetBytes(UnknownMarker);
        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        NodeEpochRf3OfflineFiles.FlushToDisk(output);
    }

    internal static async Task DeleteOwnedUnknownAsync(string directory, CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, NodeEpochRf3Protocol.UnknownEntry);
        await using var input = NodeEpochRf3OfflineFiles.OpenRead(path);
        if (input.Length is < 1 or > 256)
        { throw new IOException("The owned unknown marker changed its bounded size."); }
        var bytes = new byte[checked((int)input.Length)];
        await input.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        if (!bytes.AsSpan().SequenceEqual(System.Text.Encoding.UTF8.GetBytes(UnknownMarker)))
        { throw new IOException("The unknown node entry is no longer owned by this test."); }
        File.Delete(path);
    }

    private static async Task CopyFileAsync(string source, string destination, NodeEpochRf3FileEntry expected,
        CancellationToken cancellationToken)
    {
        await using var input = NodeEpochRf3OfflineFiles.OpenRead(source);
        if (input.Length != expected.Length)
        { throw new IOException("A node file changed after its inventory was captured."); }
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            NodeEpochRf3Protocol.BufferBytes, FileOptions.Asynchronous | FileOptions.WriteThrough);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[NodeEpochRf3Protocol.BufferBytes];
        long copied = 0;
        while (copied < expected.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0 || count > expected.Length - copied)
            { throw new IOException("A node file changed while being privately copied."); }
            digest.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            copied += count;
        }
        if (await input.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false) != 0
            || Convert.ToHexStringLower(digest.GetHashAndReset()) != expected.Sha256)
        { throw new IOException("A node file changed while being privately copied."); }
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        NodeEpochRf3OfflineFiles.FlushToDisk(output);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(destination, (UnixFileMode)expected.Mode); }
    }

    private static async Task<string> HashStableAsync(string path, long expectedLength,
        CancellationToken cancellationToken)
    {
        await using var input = NodeEpochRf3OfflineFiles.OpenRead(path);
        if (input.Length != expectedLength)
        { throw new IOException("A node file changed during inventory capture."); }
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[NodeEpochRf3Protocol.BufferBytes];
        long read = 0;
        while (read < expectedLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0 || count > expectedLength - read)
            { throw new IOException("A node file changed during inventory capture."); }
            digest.AppendData(buffer, 0, count);
            read += count;
        }
        if (await input.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false) != 0)
        { throw new IOException("A node file grew during inventory capture."); }
        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    private static string Resolve(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(static segment => segment is ".." or "." or ""))
        { throw new InvalidDataException("Node inventory path escaped its owned root."); }
        var path = Path.GetFullPath(Path.Combine(root, relative));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        { throw new InvalidDataException("Node inventory path escaped its owned root."); }
        return path;
    }

    private static int ReadMode(string path) => OperatingSystem.IsWindows()
        ? 0 : (int)File.GetUnixFileMode(path);

    private static void MakePrivateDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }

    private static void RequireDirectory(string path)
    {
        var info = new DirectoryInfo(path);
        if (!info.Exists || info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
        { throw new InvalidDataException("Node inventory root is not a regular directory."); }
    }
}
