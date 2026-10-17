using System.Text;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class StreamFixtureLifetimeTests
{
    private const string FailureDirectoryPrefix = "keyload-event-read-failure-";
    private const string ExistingEmptyDirectoryPrefix = "keyload-stream-existing-empty-";
    private const string ActiveOwnerDirectoryPrefix = "keyload-stream-active-owner-";
    private const string GuidFormat = "N";
    private const string RecordSpace = "stream-fixture-owner";
    private const string RecordId = "existing-owner";
    private const string RecordValue = "preserve-existing-record";
    private const string SentinelFileName = "owner-sentinel.txt";
    private const string SentinelValue = "preserve-existing-sentinel";
    private const int InvalidEventCount = -1;

    [Test]
    public async Task AcMp005InvalidEventCountReleasesOwnedDirectoryAndAllowsSamePathReopen()
    {
        var directory = NewDirectory(FailureDirectoryPrefix);
        try
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            {
                using var unexpected = new StreamReadResourceFixture(eventCount: InvalidEventCount, directory: directory);
            });

            await Assert.That(Directory.Exists(directory)).IsFalse();
            using var reopened = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            await Assert.That(Directory.Exists(directory)).IsTrue();
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Test]
    public async Task AcMp005ExistingEmptyDirectoryIsRejectedAndPreserved()
    {
        var directory = NewDirectory(ExistingEmptyDirectoryPrefix);
        try
        {
            Directory.CreateDirectory(directory);
            var failure = Assert.ThrowsExactly<ArgumentException>(() =>
            {
                using var unexpected = new StreamReadResourceFixture(directory: directory);
            });

            await Assert.That(failure.ParamName).IsEqualTo(nameof(directory));
            await Assert.That(Directory.Exists(directory)).IsTrue();
            await Assert.That(Directory.GetFileSystemEntries(directory)).IsEmpty();
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Test]
    public async Task AcMp005ExistingActiveStoreDirectoryPreservesOwnerRecordAndSentinel()
    {
        var directory = NewDirectory(ActiveOwnerDirectoryPrefix);
        var recordKey = KeyCodec.Encode(RecordSpace, RecordId);
        var recordBytes = Encoding.UTF8.GetBytes(RecordValue);
        var sentinelPath = Path.Combine(directory, SentinelFileName);
        try
        {
            using var owner = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            owner.Commit((transaction, _) =>
            {
                transaction.Put(recordKey, recordBytes);
                return true;
            });
            await File.WriteAllTextAsync(sentinelPath, SentinelValue);

            var failure = Assert.ThrowsExactly<ArgumentException>(() =>
            {
                using var unexpected = new StreamReadResourceFixture(directory: directory);
            });

            await Assert.That(failure.ParamName).IsEqualTo(nameof(directory));
            await Assert.That(Directory.Exists(directory)).IsTrue();
            await Assert.That(owner.Read(view => view.ReadOwnedValue(recordKey)!.SequenceEqual(recordBytes))).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(sentinelPath)).IsEqualTo(SentinelValue);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static string NewDirectory(string prefix)
    {
        string directory;
        do
        {
            directory = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString(GuidFormat));
        }
        while (Directory.Exists(directory) || File.Exists(directory));

        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
