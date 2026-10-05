using KeyLoad.CrashHost;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed class NodeEpochSourceLayoutRejectionTests
{
    private const string ForeignName = "foreign.after-boundary";
    private const string DatabaseStore = "database";
    private const string ReplicaStore = "replica";

    [Test]
    [Arguments(DatabaseStore)]
    [Arguments(ReplicaStore)]
    public async Task AcEpoch011RejectsUnknownStoreSiblingAndPreservesOriginal(string storeName)
    {
        await NodeEpochCoordinatorRejectionTests.WithNodeAsync(async (
            _, source, destination, profile, _, token) =>
        {
            var foreignPath = Path.Combine(source, storeName, ForeignName);
            byte[] foreignBytes = [0x3A, 0x00, 0xD2];
            await File.WriteAllBytesAsync(foreignPath, foreignBytes, token);
            var original = await NodeEpochInventoryCapture.CaptureAsync(source, token);
            var options = NodeEpochCrashSettings.CreateOptions(profile, destination);

            var rejected = Assert.ThrowsExactly<KeyLoadException>(() => ServerNodeFormatUpgrade.Prepare(source, RecoveryServerRuntimeOptions.Runtime(options)));

            await Assert.That(rejected.Code).IsEqualTo(ErrorCode.FormatUnsupported);
            await Assert.That(Directory.Exists(destination)).IsFalse();
            await Assert.That(Directory.Exists(destination + ServerNodeUpgradeProtocol.StageSuffix)).IsFalse();
            await NodeEpochInventoryCapture.AssertUnchangedAsync(source, original, token);
            var actualBytes = await File.ReadAllBytesAsync(foreignPath, token);
            await Assert.That(actualBytes.SequenceEqual(foreignBytes)).IsTrue();
        }, TestContext.Current!.Execution.CancellationToken);
    }
}
