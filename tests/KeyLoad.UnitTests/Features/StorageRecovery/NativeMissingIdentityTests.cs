using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NativeMissingIdentityTests
{
    private const string TreeDirectoryName = "tree";
    private const string OwnerLockFileName = "owner.lock";
    private const string UnexpectedFileName = "unexpected.data";
    private const string UnexpectedLockFileName = "unexpected.lock";
    private const string TemporaryIdentityFileName = "identity.json.tmp";
    private const string AllEntriesPattern = "*";
    private const int NoDirectory = 0;
    private const int EmptyDirectory = 1;
    private const int OwnerOnlyDirectory = 2;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcIs004And008MissingIdentityPreservesActualTreeAndJournal(bool checkpoint)
    {
        using var files = new WalFileFixture();
        using (var store = new ZoneTreeStore(new(files.DirectoryPath)))
        {
            store.Commit((transaction, _) => { transaction.Put([0x10], [0x30]); return true; });
            if (checkpoint)
            {
                store.Compact();
            }
        }
        File.Delete(files.IdentityPath);

        await AssertRejectedUnchangedAsync(files);
    }

    [Test]
    [Arguments(0)]
    [Arguments(7)]
    [Arguments(51)]
    [Arguments(WalFileFixture.HeaderBytes)]
    public async Task AcIs004And008MissingIdentityNeverTruncatesShortLegacyJournal(int length)
    {
        using var files = new WalFileFixture();
        Directory.CreateDirectory(files.DirectoryPath);
        await File.WriteAllBytesAsync(Path.Combine(files.DirectoryPath, OwnerLockFileName), []);
        var frame = WalFileFixture.CreateFrame([0x10, 0x30], magic: WalFileFixture.LegacyMagic);
        await File.WriteAllBytesAsync(files.JournalPath, frame[..length]);

        await AssertRejectedUnchangedAsync(files);
        await Assert.That(Directory.Exists(Path.Combine(files.DirectoryPath, TreeDirectoryName))).IsFalse();
    }

    [Test]
    [Arguments(TreeDirectoryName, true)]
    [Arguments(UnexpectedFileName, false)]
    [Arguments(TemporaryIdentityFileName, false)]
    public async Task AcIs008MissingIdentityRejectsEveryAmbiguousEntry(string name, bool directory)
    {
        using var files = new WalFileFixture();
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
        using var files = new WalFileFixture();
        Directory.CreateDirectory(files.DirectoryPath);
        await File.WriteAllBytesAsync(Path.Combine(files.DirectoryPath, OwnerLockFileName), [0x80, 0xFF]);

        await AssertRejectedUnchangedAsync(files);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcIs008IdentityCreationRequiresTheLiveCanonicalOwnerHandle(bool disposed)
    {
        using var files = new WalFileFixture();
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
        using var files = new WalFileFixture();
        if (initialState != NoDirectory)
        {
            Directory.CreateDirectory(files.DirectoryPath);
        }
        if (initialState == OwnerOnlyDirectory)
        {
            await File.WriteAllBytesAsync(Path.Combine(files.DirectoryPath, OwnerLockFileName), []);
        }
        var identity = files.Initialize();

        await Assert.That(identity.FormatVersion).IsEqualTo(WalFileFixture.CurrentIdentityVersion);
        await Assert.That(File.Exists(files.IdentityPath)).IsTrue();
        await Assert.That(File.Exists(files.JournalPath)).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(files.DirectoryPath, TreeDirectoryName))).IsTrue();
        using var reopened = new ZoneTreeStore(new(files.DirectoryPath));
        await WalFileFixture.AssertPreservedIdentity(identity, reopened.Identity);
        await Assert.That(reopened.Position).IsEqualTo(0);
    }

    private static async Task AssertRejectedUnchangedAsync(WalFileFixture files)
    {
        var before = await files.CaptureFilesAsync();
        var directories = Directory.EnumerateDirectories(files.DirectoryPath, AllEntriesPattern,
            SearchOption.AllDirectories).ToArray();
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var attempted = new ZoneTreeStore(new(files.DirectoryPath));
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
