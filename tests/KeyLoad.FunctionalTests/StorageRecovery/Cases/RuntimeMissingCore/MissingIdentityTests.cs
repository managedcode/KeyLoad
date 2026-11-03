using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class MissingIdentityTests
{
    private const string TreeDirectoryName = "tree";
    private const string PlainTreeDirectoryName = "plain-tree";
    private const string IdentityFileName = "identity.json";
    private const string OwnerLockFileName = "owner.lock";
    private const string UnexpectedFileName = "unexpected.data";
    private const string UnexpectedLockFileName = "unexpected.lock";
    private const string TemporaryIdentityFileName = "identity.json.tmp";
    private const string AllEntriesPattern = "*";
    private const int NoDirectory = 0;
    private const int EmptyDirectory = 1;
    private const int OwnerOnlyDirectory = 2;

    [Test]
    public async Task AcIs004And008MissingIdentityPreservesActualTree()
    {
        using var files = new StoreFileFixture();
        StoreIdentity originalIdentity;
        long originalPosition;
        using (var store = new ZoneTreeStore(new(files.DirectoryPath), UnitExecutionOptions.StorageExecution()))
        {
            store.Commit((transaction, _) => { transaction.Put([0x10], [0x30]); return true; });
            originalIdentity = store.Identity;
            originalPosition = store.Position;
        }
        var token = TestContext.Current!.Execution.CancellationToken;
        var identityBytes = await File.ReadAllBytesAsync(files.IdentityPath, token);
        File.Delete(files.IdentityPath);

        await AssertRejectedUnchangedAsync(files);
        await File.WriteAllBytesAsync(files.IdentityPath, identityBytes, token);
        await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath, token))
            .IsEquivalentTo(identityBytes, CollectionOrdering.Matching);
        await IdentityRepairContinuation.RequireCurrentAsync(files, originalIdentity, originalPosition,
            token).ConfigureAwait(false);
    }

    [Test]
    [Arguments(TreeDirectoryName, true)]
    [Arguments(UnexpectedFileName, false)]
    [Arguments(TemporaryIdentityFileName, false)]
    public async Task AcIs008MissingIdentityRejectsEveryAmbiguousEntry(string name, bool directory)
    {
        using var files = new StoreFileFixture();
        Directory.CreateDirectory(files.DirectoryPath);
        await File.WriteAllBytesAsync(Path.Combine(files.DirectoryPath, OwnerLockFileName), []);
        var path = Path.Combine(files.DirectoryPath, name);
        if (directory)
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            await File.WriteAllBytesAsync(path, [0x80, 0xFF]);
        }

        await AssertRejectedUnchangedAsync(files);
    }

    [Test]
    public async Task AcIs008OwnerNameWithUnexpectedContentsIsNotAnEmptyStore()
    {
        using var files = new StoreFileFixture();
        Directory.CreateDirectory(files.DirectoryPath);
        await File.WriteAllBytesAsync(Path.Combine(files.DirectoryPath, OwnerLockFileName), [0x80, 0xFF]);

        await AssertRejectedUnchangedAsync(files);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcIs008IdentityCreationRequiresTheLiveCanonicalOwnerHandle(bool disposed)
    {
        using var files = new StoreFileFixture();
        Directory.CreateDirectory(files.DirectoryPath);
        var name = disposed ? OwnerLockFileName : UnexpectedLockFileName;
        await using var ownership = new FileStream(Path.Combine(files.DirectoryPath, name),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        if (disposed)
        {
            await ownership.DisposeAsync();
        }

        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            ZoneTreeIdentityFile.Open(new(files.DirectoryPath), ownership));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(File.Exists(files.IdentityPath)).IsFalse();
        await Assert.That(File.Exists(Path.Combine(files.DirectoryPath, TemporaryIdentityFileName))).IsFalse();
        await Assert.That(Directory.EnumerateFileSystemEntries(files.DirectoryPath).Count()).IsEqualTo(1);
    }

    [Test]
    [Arguments(NoDirectory)]
    [Arguments(EmptyDirectory)]
    [Arguments(OwnerOnlyDirectory)]
    public async Task AcIs004BrandNewDirectoryCreatesCurrentIdentityAndPreservesReopen(int initialState)
    {
        using var files = new StoreFileFixture();
        if (initialState != NoDirectory)
        {
            Directory.CreateDirectory(files.DirectoryPath);
        }
        if (initialState == OwnerOnlyDirectory)
        {
            await File.WriteAllBytesAsync(Path.Combine(files.DirectoryPath, OwnerLockFileName), []);
        }
        var identity = files.Initialize();

        await Assert.That(Directory.EnumerateFileSystemEntries(files.DirectoryPath)
            .Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal))
            .IsEquivalentTo([IdentityFileName, OwnerLockFileName, PlainTreeDirectoryName, TreeDirectoryName], CollectionOrdering.Matching);
        await Assert.That(Directory.Exists(Path.Combine(files.DirectoryPath, TreeDirectoryName))).IsTrue();
        using var reopened = new ZoneTreeStore(new(files.DirectoryPath), UnitExecutionOptions.StorageExecution());
        await StoreFileFixture.AssertPreservedIdentity(identity, reopened.Identity);
        await Assert.That(reopened.Position).IsEqualTo(0);
    }

    private static async Task AssertRejectedUnchangedAsync(StoreFileFixture files)
    {
        var before = await files.CaptureFilesAsync();
        var directories = Directory.EnumerateDirectories(files.DirectoryPath, AllEntriesPattern,
            SearchOption.AllDirectories).ToArray();
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var attempted = new ZoneTreeStore(new(files.DirectoryPath), UnitExecutionOptions.StorageExecution());
        });

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(File.Exists(files.IdentityPath)).IsFalse();
        await files.AssertFilesUnchangedAsync(before);
        await Assert.That(Directory.EnumerateDirectories(files.DirectoryPath, AllEntriesPattern,
            SearchOption.AllDirectories)).IsEquivalentTo(directories, CollectionOrdering.Matching);
        await using var ownership = new FileStream(Path.Combine(files.DirectoryPath, OwnerLockFileName),
            FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await Assert.That(ownership.CanWrite).IsTrue();
    }
}
