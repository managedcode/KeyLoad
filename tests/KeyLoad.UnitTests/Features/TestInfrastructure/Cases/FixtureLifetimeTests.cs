using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal sealed class FixtureLifetimeTests
{
    private const string DirectoryPrefix = "keyload-fixture-lifetime-";
    private const string GuidFormat = "N";
    private const int ZeroConcurrency = 0;
    private const int NegativeConcurrency = -1;
    private const string DirectoryParameter = "directory";
    private const string SentinelFileName = "existing-owner.txt";
    private const string SentinelContent = "Preserve the existing owner and its files.";
    private const byte ExistingKey = 0x21;
    private const byte ExistingValue = 0x42;
    private const long ExistingPosition = 1;

    [Test]
    [Arguments(ZeroConcurrency)]
    [Arguments(NegativeConcurrency)]
    public async Task AcTest006_FailedFixtureSetupReleasesStoreAndOwnedDirectory(int concurrency)
    {
        var directory = NewDirectory();
        try
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            {
                using var unexpected = new TestDatabase(new() { MaxConcurrentQueries = concurrency }, directory);
            });
            await Assert.That(Directory.Exists(directory)).IsFalse();
            using var reopened = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            await Assert.That(reopened.Position).IsEqualTo(0L);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Test]
    public async Task AcTest006_SuccessfulFixtureOwnsItsDirectoryUntilDisposal()
    {
        var directory = NewDirectory();
        try
        {
            using (var database = new TestDatabase(directory: directory))
            {
                await Assert.That(Directory.Exists(directory)).IsTrue();
                await Assert.That(database.Store.Position).IsGreaterThan(0L);
            }
            await Assert.That(Directory.Exists(directory)).IsFalse();
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Test]
    public async Task AcTest006_ExistingEmptyDirectoryIsRejectedBeforeAcquisition()
    {
        var directory = NewDirectory();
        Directory.CreateDirectory(directory);
        try
        {
            var error = Assert.ThrowsExactly<ArgumentException>(() =>
            {
                using var unexpected = new TestDatabase(directory: directory);
            });
            await Assert.That(error.ParamName).IsEqualTo(DirectoryParameter);
            await Assert.That(Directory.Exists(directory)).IsTrue();
            await Assert.That(Directory.EnumerateFileSystemEntries(directory).Any()).IsFalse();
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Test]
    public async Task AcTest006_ExistingActiveOwnerAndFilesSurviveFixtureRejection()
    {
        var directory = NewDirectory();
        Directory.CreateDirectory(directory);
        var sentinel = Path.Combine(directory, SentinelFileName);
        try
        {
            using var owner = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            await File.WriteAllTextAsync(sentinel, SentinelContent);
            owner.Commit((transaction, _) =>
            {
                transaction.Put([ExistingKey], [ExistingValue]);
                return true;
            });
            var error = Assert.ThrowsExactly<ArgumentException>(() =>
            {
                using var unexpected = new TestDatabase(new() { MaxConcurrentQueries = ZeroConcurrency }, directory);
            });
            await Assert.That(error.ParamName).IsEqualTo(DirectoryParameter);
            await Assert.That(await File.ReadAllTextAsync(sentinel)).IsEqualTo(SentinelContent);
            await Assert.That(owner.Position).IsEqualTo(ExistingPosition);
            await Assert.That(owner.Read(view => view.ReadOwnedValue([ExistingKey])))
                .IsEquivalentTo(new byte[] { ExistingValue }, CollectionOrdering.Matching);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static string NewDirectory()
        => Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
