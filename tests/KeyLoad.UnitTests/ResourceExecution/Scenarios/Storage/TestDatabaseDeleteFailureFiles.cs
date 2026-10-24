using KeyLoad.Server;
using KeyLoad.UnitTests.StorageRecovery;

namespace KeyLoad.UnitTests.ResourceExecution;

internal static class TestDatabaseDeleteFailureFiles
{
    internal static async Task<PartitionIdentityInventoryCut> CaptureAsync(TestDatabase fixture, CancellationToken token)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(TestDatabaseDeleteFailureProtocol.UnixRequired);
        }
        var limits = fixture.Database.Limits;
        var paths = CollectOwnedPaths(fixture.Directory, limits.MaxScanRecords);
        var bytes = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        var modes = new Dictionary<string, UnixFileMode>(StringComparer.Ordinal)
        {
            [string.Empty] = File.GetUnixFileMode(fixture.Directory)
        };
        var remaining = limits.MaxQueryReadBytes;
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != default)
            {
                throw new InvalidOperationException(TestDatabaseDeleteFailureProtocol.EntryRequired);
            }
            var relative = Path.GetRelativePath(fixture.Directory, path);
            modes.Add(relative, File.GetUnixFileMode(path));
            var value = (attributes & FileAttributes.Directory) != default ? null
                : await ReadAsync(path, remaining, fixture.StorageExecution.Value.FileBufferBytes, token);
            bytes.Add(relative, value);
            remaining = checked(remaining - (value?.LongLength ?? default));
        }
        return new(bytes, modes);
    }

    private static List<string> CollectOwnedPaths(string root, int maximumEntries)
    {
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != default)
        {
            throw new InvalidOperationException(TestDatabaseDeleteFailureProtocol.EntryRequired);
        }
        var paths = new List<string>();
        var pending = new Queue<string>();
        pending.Enqueue(root);
        var refusalCount = checked((long)maximumEntries + TestDatabaseDeleteFailureProtocol.InventoryLookahead);
        while (pending.TryDequeue(out var directory))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory,
                TestDatabaseDeleteFailureProtocol.SearchPattern, SearchOption.TopDirectoryOnly))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != default)
                {
                    throw new InvalidOperationException(TestDatabaseDeleteFailureProtocol.EntryRequired);
                }
                paths.Add(path);
                if (paths.Count >= refusalCount)
                {
                    throw new InvalidOperationException(TestDatabaseDeleteFailureProtocol.InventoryExceeded);
                }
                if ((attributes & FileAttributes.Directory) != default)
                {
                    pending.Enqueue(path);
                }
            }
        }
        return paths;
    }

    private static async Task<byte[]> ReadAsync(string path, long remaining, int bufferBytes, CancellationToken token)
    {
        var failures = new List<Exception>();
        var lifetime = new StorageFailureOwnerLifetime();
        byte[]? bytes = null;
        var input = lifetime.Acquire(() => new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous,
            BufferSize = bufferBytes
        }), failures);
        if (input is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var length = input.Length;
                if (length > remaining || length > int.MaxValue)
                {
                    throw new InvalidOperationException(TestDatabaseDeleteFailureProtocol.InventoryExceeded);
                }
                bytes = new byte[checked((int)length)];
                await input.ReadExactlyAsync(bytes, token);
                await Assert.That(input.Length).IsEqualTo(length);
            }, failures);
            lifetime.Close(input, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return bytes ?? throw new InvalidOperationException(TestDatabaseDeleteFailureProtocol.InventoryExceeded);
    }
}
