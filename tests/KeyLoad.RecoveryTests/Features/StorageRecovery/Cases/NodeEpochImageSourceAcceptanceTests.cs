using KeyLoad.CrashHost;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed class NodeEpochImageSourceAcceptanceTests
{
    private const string TrialPrefix = "keyload-node-epoch-image-source-";
    private const string SourceName = "native-source";
    private const string ImageName = "native-source-image.bin";
    private const string ConvertedName = "native7-image.bin";
    private const string JournalName = "commands.wal";
    private const int Native5DataEpoch = 5;
    private const int Native6DataEpoch = 6;
    private const long ExpectedRecordCount = 4;
    private const int ProbeTimeoutSeconds = 25;
    private const int CleanupSeconds = 30;

    [Test]
    [Arguments(Native5DataEpoch)]
    [Arguments(Native6DataEpoch)]
    public async Task AcEpoch7ActualPriorCheckpointImageVerifiesWithoutChangingItsSource(int dataEpoch)
    {
        await RunTrialAsync(VerifyActualImageAsync, dataEpoch, TestContext.Current!.Execution.CancellationToken);
    }

    private static async Task VerifyActualImageAsync(string root, int dataEpoch, CancellationToken cancellationToken)
    {
        var source = Path.Combine(root, SourceName);
        var image = Path.Combine(root, ImageName);
        var converted = Path.Combine(root, ConvertedName);
        var prior = await EpochPriorExecutableFixture.CreateAsync(source, true, cancellationToken, dataEpoch);
        var sourceInventory = await EpochUpgradeFileInventory.CaptureAsync(source, cancellationToken);
        File.Copy(Path.Combine(source, JournalName), image);
        var originalImage = await File.ReadAllBytesAsync(image, cancellationToken);

        var oldReader = await EpochPriorExecutableFixture.VerifySnapshotAsync(source, image, cancellationToken, dataEpoch);
        await AssertPriorReaderAsync(prior, oldReader);
        var snapshot = ZoneTreeSnapshotFormatUpgrade.VerifySource(image, prior.Incarnation, dataEpoch);
        await AssertCurrentReader(prior, snapshot);
        var convertedSnapshot = ZoneTreeSnapshotFormatUpgrade.Upgrade(image, converted, prior.Incarnation, dataEpoch);
        await AssertCurrentReader(prior, convertedSnapshot);
        await AssertCurrentFileReader(root, converted, prior);
        var oldReaderRejectsCurrent = await EpochPriorExecutableFixture.VerifySnapshotAsync(source, converted,
            cancellationToken, dataEpoch);
        await Assert.That(oldReaderRejectsCurrent.ErrorCode).IsEqualTo(ErrorCode.FormatUnsupported.ToString());

        var unchangedImage = await File.ReadAllBytesAsync(image, cancellationToken);
        await Assert.That(unchangedImage.SequenceEqual(originalImage)).IsTrue();
        await EpochUpgradeFileInventory.AssertUnchangedAsync(source, sourceInventory, cancellationToken);
    }

    private static async Task AssertPriorReaderAsync(EpochPriorProbeReceipt expected,
        EpochPriorProbeReceipt actual)
    {
        await Assert.That(actual.ErrorCode).IsNull();
        await Assert.That(actual.SourceRevision).IsEqualTo(expected.SourceRevision);
        await Assert.That(actual.DataEpoch).IsEqualTo(expected.DataEpoch);
        await Assert.That(actual.NodeId).IsEqualTo(expected.NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(expected.Incarnation);
        await Assert.That(actual.Position).IsEqualTo(expected.Position);
        await Assert.That(actual.AppliedPosition).IsEqualTo(expected.AppliedPosition);
    }

    private static async Task AssertCurrentReader(EpochPriorProbeReceipt expected, StorageSnapshot actual)
    {
        await Assert.That(actual.Incarnation).IsEqualTo(expected.Incarnation);
        await Assert.That(actual.Position).IsEqualTo(expected.Position);
        await Assert.That(actual.AppliedPosition).IsEqualTo(expected.AppliedPosition);
        await Assert.That(actual.RecordCount).IsEqualTo(ExpectedRecordCount);
    }

    private static async Task AssertCurrentFileReader(string root, string path, EpochPriorProbeReceipt expected)
    {
        using var store = new ZoneTreeStore(new(Path.Combine(root, "current-verifier"))
        { Incarnation = expected.Incarnation });
        var actual = store.VerifySnapshot(path);
        await AssertCurrentReader(expected, actual);
        _ = store.InstallSnapshot(path, expected.AppliedPosition);
        await AssertSeedRowsAsync(store);
    }

    private static async Task AssertSeedRowsAsync(ZoneTreeStore store)
    {
        foreach (var expected in EpochUpgradeFixture.RawRecords())
        {
            var value = store.Read(view => view.ReadOwnedValue(expected.Key))
                ?? throw new InvalidDataException("The converted image is missing a seeded canonical row.");
            await Assert.That(value.SequenceEqual(expected.Value)).IsTrue();
        }
    }

    private static async Task RunTrialAsync(Func<string, int, CancellationToken, Task> trial, int dataEpoch,
        CancellationToken callerToken)
    {
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        var root = Path.Combine(Path.GetTempPath(), TrialPrefix + Guid.NewGuid().ToString("N"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(ProbeTimeoutSeconds));
        Exception? activeFailure = null;
        try
        {
            Directory.CreateDirectory(root);
            await trial(root, dataEpoch, timeout.Token);
        }
        catch (Exception failure)
        {
            activeFailure = failure;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupSeconds));
            await EpochUpgradeCleanup.SettleAsync(null, root, Path.Combine(root, SourceName),
                activeFailure, cleanup.Token);
        }
    }
}
