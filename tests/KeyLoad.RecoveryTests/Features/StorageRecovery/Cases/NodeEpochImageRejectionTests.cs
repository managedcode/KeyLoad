using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed class NodeEpochImageRejectionTests
{
    private const string TrialPrefix = "keyload-node-epoch-image-reject-";
    private const string SourceName = "native5-source";
    private const string ImageName = "native3-image.bin";
    private const string JournalName = "commands.wal";
    private const string OutputName = "current4-image.bin";
    private const int TrialTimeoutSeconds = 45;
    private const int CleanupTimeoutSeconds = 30;

    [Test]
    public async Task AcEpoch007RejectsCorruptAndTrailingImagesWithoutOutputOrMutation()
    {
        await RunTrialAsync(async (root, token) =>
        {
            var source = Path.Combine(root, SourceName);
            var image = Path.Combine(root, ImageName);
            var corrupt = Path.Combine(root, "corrupt-image.bin");
            var trailing = Path.Combine(root, "trailing-image.bin");
            var output = Path.Combine(root, OutputName);
            var prior = await EpochPriorExecutableFixture.CreateAsync(source, true, token);
            File.Copy(Path.Combine(source, JournalName), image);
            var sourceInventory = await EpochUpgradeFileInventory.CaptureAsync(source, token);
            await CreateInvalidImagesAsync(image, corrupt, trailing, token);
            var corruptBytes = await File.ReadAllBytesAsync(corrupt, token);
            var trailingBytes = await File.ReadAllBytesAsync(trailing, token);

            await AssertRejectedWithoutOutputAsync(corrupt, prior.Incarnation, output, ErrorCode.Corruption);
            await AssertRejectedWithoutOutputAsync(trailing, prior.Incarnation, output, ErrorCode.Corruption);
            await AssertImageUnchangedAsync(corrupt, corruptBytes, token);
            await AssertImageUnchangedAsync(trailing, trailingBytes, token);
            await EpochUpgradeFileInventory.AssertUnchangedAsync(source, sourceInventory, token);
        }, TestContext.Current!.Execution.CancellationToken);
    }

    [Test]
    public async Task AcEpoch007RequiresMatchingIncarnationBeforeConvertingImage()
    {
        await RunTrialAsync(async (root, token) =>
        {
            var source = Path.Combine(root, SourceName);
            var image = Path.Combine(root, ImageName);
            var output = Path.Combine(root, OutputName);
            var prior = await EpochPriorExecutableFixture.CreateAsync(source, true, token);
            File.Copy(Path.Combine(source, JournalName), image);
            var imageBytes = await File.ReadAllBytesAsync(image, token);
            var sourceInventory = await EpochUpgradeFileInventory.CaptureAsync(source, token);
            var wrongIncarnation = Guid.NewGuid();

            var rejected = Assert.ThrowsExactly<KeyLoadException>(() =>
                ZoneTreeSnapshotFormatUpgrade.Upgrade(image, output, wrongIncarnation));

            await Assert.That(rejected.Code).IsEqualTo(ErrorCode.TokenInvalidated);
            await Assert.That(wrongIncarnation).IsNotEqualTo(prior.Incarnation);
            await Assert.That(File.Exists(output)).IsFalse();
            await Assert.That((await File.ReadAllBytesAsync(image, token)).SequenceEqual(imageBytes)).IsTrue();
            await EpochUpgradeFileInventory.AssertUnchangedAsync(source, sourceInventory, token);
        }, TestContext.Current!.Execution.CancellationToken);
    }

    [Test]
    public async Task AcEpoch007EnforcesConfiguredImageByteBoundBeforeCreatingOutput()
    {
        await RunTrialAsync(async (root, token) =>
        {
            var source = Path.Combine(root, SourceName);
            var image = Path.Combine(root, ImageName);
            var output = Path.Combine(root, OutputName);
            var prior = await EpochPriorExecutableFixture.CreateAsync(source, true, token);
            File.Copy(Path.Combine(source, JournalName), image);
            var originalImage = await File.ReadAllBytesAsync(image, token);
            var sourceInventory = await EpochUpgradeFileInventory.CaptureAsync(source, token);
            var options = new ZoneTreeSnapshotUpgradeOptions { MaxSnapshotBytes = originalImage.Length - 1 };

            var rejected = Assert.ThrowsExactly<KeyLoadException>(() =>
                ZoneTreeSnapshotFormatUpgrade.Upgrade(image, output, prior.Incarnation, options));

            await Assert.That(rejected.Code).IsEqualTo(ErrorCode.ResourceExhausted);
            await Assert.That(File.Exists(output)).IsFalse();
            await Assert.That((await File.ReadAllBytesAsync(image, token)).SequenceEqual(originalImage)).IsTrue();
            await EpochUpgradeFileInventory.AssertUnchangedAsync(source, sourceInventory, token);
        }, TestContext.Current!.Execution.CancellationToken);
    }

    private static async Task CreateInvalidImagesAsync(string image, string corrupt, string trailing,
        CancellationToken cancellationToken)
    {
        var validBytes = await File.ReadAllBytesAsync(image, cancellationToken);
        var corruptBytes = validBytes.ToArray();
        corruptBytes[^1] ^= 0x01;
        await File.WriteAllBytesAsync(corrupt, corruptBytes, cancellationToken);
        var trailingBytes = new byte[validBytes.Length + 1];
        validBytes.CopyTo(trailingBytes, 0);
        trailingBytes[^1] = 0xA5;
        await File.WriteAllBytesAsync(trailing, trailingBytes, cancellationToken);
    }

    private static async Task AssertRejectedWithoutOutputAsync(string image, Guid incarnation, string output, ErrorCode code)
    {
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() =>
            ZoneTreeSnapshotFormatUpgrade.Upgrade(image, output, incarnation));
        await Assert.That(rejected.Code).IsEqualTo(code);
        await Assert.That(File.Exists(output)).IsFalse();
    }

    private static async Task AssertImageUnchangedAsync(string image, byte[] expected,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(image, cancellationToken);
        await Assert.That(bytes.SequenceEqual(expected)).IsTrue();
    }

    private static async Task RunTrialAsync(Func<string, CancellationToken, Task> trial,
        CancellationToken callerToken)
    {
        using var admission = await StorageTrialLease.AcquireAsync(callerToken);
        var root = Path.Combine(Path.GetTempPath(), TrialPrefix + Guid.NewGuid().ToString("N"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(TrialTimeoutSeconds));
        Exception? activeFailure = null;
        try
        {
            Directory.CreateDirectory(root);
            await trial(root, timeout.Token);
        }
        catch (Exception failure)
        {
            activeFailure = failure;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupTimeoutSeconds));
            await EpochUpgradeCleanup.SettleAsync(null, root, Path.Combine(root, SourceName), activeFailure,
                cleanup.Token);
        }
    }
}
