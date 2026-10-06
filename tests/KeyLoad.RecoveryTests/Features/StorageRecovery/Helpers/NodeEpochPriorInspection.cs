using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class NodeEpochPriorInspection
{
    private static readonly string[] AuthorityFiles = ["identity.json", "commands.wal"];

    internal static async Task<EpochPriorProbeReceipt> InspectCopyAsync(string source,
        NodeEpochInventory original, int dataEpoch, CancellationToken cancellationToken)
    {
        var trial = Path.GetDirectoryName(source)
            ?? throw new InvalidDataException("The prior node has no owned trial directory.");
        var copy = Path.Combine(trial, "prior-reader-" + Guid.NewGuid().ToString("N"));
        var executionOptions = RecoveryExecutionOptions.NodeUpgrade();
        ServerNodeUpgradeFiles.CreatePrivateDirectory(copy);
        foreach (var name in AuthorityFiles)
        {
            ServerNodeUpgradeFiles.Copy(Path.Combine(source, ServerNodeUpgradeProtocol.Canonical, name),
                Path.Combine(copy, name), executionOptions);
        }
        ServerNodeUpgradeFiles.CreateEmpty(Path.Combine(copy, "owner.lock"), executionOptions);
        var copied = await EpochUpgradeFileInventory.CaptureAsync(copy, cancellationToken);
        foreach (var name in AuthorityFiles)
        {
            await Assert.That(copied[name])
                .IsEqualTo(original.Files[Path.Combine(ServerNodeUpgradeProtocol.Canonical, name)]);
        }
        var receipt = await EpochPriorExecutableFixture.InspectAsync(copy, cancellationToken, dataEpoch);
        await KilledProcessFileReadiness.WaitAsync(copy, cancellationToken);
        EpochUpgradeFileInventory.AssertNativeHandlesReleased(copy);
        await NodeEpochInventoryCapture.AssertUnchangedAsync(source, original, cancellationToken);
        return receipt;
    }
}
