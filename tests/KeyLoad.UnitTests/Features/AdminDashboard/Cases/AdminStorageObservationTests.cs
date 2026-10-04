using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.AdminDashboard;

internal sealed class AdminStorageObservationTests
{
    private const string Prefix = "keyload-admin-files-";
    private const string FileName = "observed.bin";
    private const int FileBytes = 257;
    private const int BoundExceededFiles = 2_100;

    [Test]
    public async Task AcAd002RealFileLengthAndMissingDirectoryAreHonest()
    {
        var root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllBytesAsync(Path.Combine(root, FileName), new byte[FileBytes]);
            var snapshot = AdminStorageObserver.Read(root, CancellationToken.None);
            await Assert.That(snapshot.Complete).IsTrue();
            await Assert.That(snapshot.TotalBytes).IsEqualTo(FileBytes);
            await Assert.That(snapshot.Files.Single().Path).IsEqualTo(FileName);
            await Assert.That(snapshot.Files.Single().Bytes).IsEqualTo(FileBytes);
            Directory.Delete(root, recursive: true);
            var missing = AdminStorageObserver.Read(root, CancellationToken.None);
            await Assert.That(missing.Complete).IsFalse();
            await Assert.That(missing.TotalBytes).IsNull();
            await Assert.That(string.IsNullOrWhiteSpace(missing.Notice)).IsFalse();
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    }

    [Test]
    public async Task AcAd002EnumerationLimitAndCancellationNeverProduceCompleteZero()
    {
        var root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            for (var index = 0; index < BoundExceededFiles; index++)
            { await File.WriteAllBytesAsync(Path.Combine(root, index.ToString(System.Globalization.CultureInfo.InvariantCulture)), [1]); }
            var snapshot = AdminStorageObserver.Read(root, CancellationToken.None);
            await Assert.That(snapshot.Complete).IsFalse();
            await Assert.That(snapshot.Files.Length).IsLessThanOrEqualTo(200);
            await Assert.That(snapshot.ObservedFiles).IsLessThanOrEqualTo(2_048);
            await Assert.That(string.IsNullOrWhiteSpace(snapshot.Notice)).IsFalse();
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();
            Assert.ThrowsExactly<OperationCanceledException>(() => AdminStorageObserver.Read(root, cancelled.Token));
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    }

    [Test]
    public async Task AcAd002SymbolicFileIsNotFollowedOrReportedAsComplete()
    {
        var root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
        var external = root + "-external";
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(external);
            var target = Path.Combine(external, FileName);
            await File.WriteAllBytesAsync(target, new byte[FileBytes]);
            File.CreateSymbolicLink(Path.Combine(root, FileName), target);
            var snapshot = AdminStorageObserver.Read(root, CancellationToken.None);
            await Assert.That(snapshot.Complete).IsFalse();
            await Assert.That(snapshot.Files).IsEmpty();
            await Assert.That(snapshot.ObservedFiles).IsEqualTo(0);
            await Assert.That(string.IsNullOrWhiteSpace(snapshot.Notice)).IsFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            { Directory.Delete(root, recursive: true); }
            if (Directory.Exists(external))
            { Directory.Delete(external, recursive: true); }
        }
    }
}
