using System.Text;
using KeyLoad.CrashHost;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class ZoneTreeExistingStoreFixture : IDisposable
{
    internal const string OwnerFile = "owner.lock";
    internal const string IdentityFile = "identity.json";
    internal const string JournalFile = "commands.wal";
    internal const string TreeDirectory = "tree";
    internal const string OuterOwnerFile = "node.owner.lock";
    internal const int LegacyFormat = 4;
    internal const int CurrentFormat = 7;
    internal const int CompleteHeaderBytes = 52;
    private const string AllEntriesPattern = "*";
    private const string TemporaryPrefix = "keyload-existing-store-";
    private const string CanonicalDirectory = "database";
    private const string HiddenSuffix = ".original";
    private const string GuidFormat = "N";
    private const string KeyText = "guard/committed";
    private const string ValueText = "original committed record";
    private readonly string root = Path.Combine(Path.GetTempPath(), TemporaryPrefix + Guid.NewGuid().ToString(GuidFormat));

    internal ZoneTreeExistingStoreFixture()
    {
        Directory.CreateDirectory(root);
        DirectoryPath = Path.Combine(root, CanonicalDirectory);
        OuterOwnerPath = Path.Combine(root, OuterOwnerFile);
        using (File.Create(OuterOwnerPath))
        { }
        Options = new(DirectoryPath) { Incarnation = Guid.NewGuid() };
        using var original = new ZoneTreeStore(Options);
        Identity = original.Identity;
        original.Commit((transaction, _) => { transaction.Put(Key, Value); return true; });
    }

    internal string DirectoryPath { get; }
    internal string OuterOwnerPath { get; }
    internal ZoneTreeStoreOptions Options { get; }
    internal StoreIdentity Identity { get; }
    internal byte[] Key { get; } = Encoding.UTF8.GetBytes(KeyText);
    internal byte[] Value { get; } = Encoding.UTF8.GetBytes(ValueText);
    internal string IdentityPath => Path.Combine(DirectoryPath, IdentityFile);
    internal string HiddenIdentityPath => IdentityPath + HiddenSuffix;
    internal string JournalPath => Path.Combine(DirectoryPath, JournalFile);

    internal Task<ExistingStoreInspectorExit> InspectAsync(
        ExistingStoreInspectionVariant variant = ExistingStoreInspectionVariant.Normal,
        Guid? expectedNodeId = null,
        Guid? incarnation = null,
        string? directory = null,
        bool cancelWhenReady = false,
        CancellationToken cancellationToken = default)
    {
        var request = new ExistingStoreInspectionRequest(directory ?? DirectoryPath,
            expectedNodeId ?? Identity.NodeId, incarnation ?? Identity.Incarnation, variant);
        return ExistingStoreInspectorProcess.RunAsync(request, OuterOwnerPath, cancelWhenReady, cancellationToken);
    }

    internal async Task AssertOwnerAvailableAsync()
    {
        await using var owner = new FileStream(Path.Combine(DirectoryPath, OwnerFile), FileMode.Open,
            FileAccess.ReadWrite, FileShare.None);
    }

    internal async Task<IReadOnlyDictionary<string, byte[]?>> CaptureStoreEntriesAsync()
    {
        var snapshot = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateDirectories(DirectoryPath, AllEntriesPattern, SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            snapshot.Add(Path.GetRelativePath(DirectoryPath, path), null);
        }
        foreach (var path in Directory.EnumerateFiles(DirectoryPath, AllEntriesPattern, SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            snapshot.Add(Path.GetRelativePath(DirectoryPath, path), await File.ReadAllBytesAsync(path));
        }
        return snapshot;
    }

    internal async Task AssertStoreEntriesUnchangedAsync(IReadOnlyDictionary<string, byte[]?> expected)
    {
        var actual = await CaptureStoreEntriesAsync();
        await Assert.That(actual.Keys).IsEquivalentTo(expected.Keys, CollectionOrdering.Matching);
        foreach (var (path, bytes) in expected)
        {
            if (bytes is null)
            {
                await Assert.That(actual[path]).IsNull();
            }
            else
            {
                await Assert.That(actual[path]).IsEquivalentTo(bytes, CollectionOrdering.Matching);
            }
        }
    }

    internal string HideFile(string name)
    {
        var path = Path.Combine(DirectoryPath, name);
        File.Move(path, path + HiddenSuffix);
        return path;
    }

    internal static void HideDirectory(string path) => Directory.Move(path, path + HiddenSuffix);

    internal void HideProviderMetadata()
    {
        var path = Path.Combine(DirectoryPath, TreeDirectory);
        HideDirectory(path);
        Directory.CreateDirectory(path);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
