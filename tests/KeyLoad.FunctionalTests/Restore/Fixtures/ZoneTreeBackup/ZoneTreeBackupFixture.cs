using KeyLoad.ProcessRecoveryTests.StorageRecovery;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.BackupRestore;

/// <summary>Owns one source store, its backup and restore destinations below the test output directory.</summary>
internal sealed class ZoneTreeBackupFixture : IDisposable
{
    internal const string BeforeNamespace = "backup-before";
    internal const string AfterNamespace = "backup-after";
    internal const string HealthyNamespace = "backup-healthy";
    private const string RootPrefix = "keyload-zonetree-backup-";
    private const string AllFiles = "*";
    private readonly string root = Path.Combine(StorageTrialDirectory.ResolveExistingDirectory(new(AppContext.BaseDirectory)),
        RootPrefix + Guid.NewGuid().ToString("N"));

    internal ZoneTreeBackupFixture()
    {
        Directory.CreateDirectory(root);
    }

    internal string Source => Path.Combine(root, "source");
    internal string Backup => Path.Combine(root, "backup");
    internal string Restored => Path.Combine(root, "restored");
    internal string Copy => Path.Combine(root, "copy");

    internal string Entry(string name) => Path.Combine(root, name);

    internal static ZoneTreeStore Open(string directory) => new(new(directory),
        UnitExecutionOptions.StorageExecution());

    internal static byte[] Key(string space, long index) => KeyCodec.Encode(space, index);

    internal static long Commit(ZoneTreeStore store, string space, long index) => store.Commit((transaction, position) =>
    {
        transaction.PutRecord(Key(space, index), space + index);
        return position;
    });

    internal static string? Read(ZoneTreeStore store, string space, long index)
        => store.Read(view => view.ReadOwnedValue(Key(space, index)) is { } bytes
            ? OrleansSerialization.Deserialize<string>(bytes) : null);

    internal static async Task<SortedDictionary<string, byte[]>> CaptureAsync(string directory, CancellationToken token)
    {
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(directory, AllFiles, SearchOption.AllDirectories))
        {
            files.Add(Path.GetRelativePath(directory, file), await File.ReadAllBytesAsync(file, token));
        }
        return files;
    }

    internal static async Task SameFilesAsync(IReadOnlyDictionary<string, byte[]> expected, string directory,
        CancellationToken token)
    {
        var actual = await CaptureAsync(directory, token);
        await Assert.That(actual.Keys.ToArray()).IsEquivalentTo(expected.Keys.Order(StringComparer.Ordinal).ToArray(),
            CollectionOrdering.Matching);
        foreach (var file in expected)
        {
            await Assert.That(actual[file.Key]).IsEquivalentTo(file.Value, CollectionOrdering.Matching);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
