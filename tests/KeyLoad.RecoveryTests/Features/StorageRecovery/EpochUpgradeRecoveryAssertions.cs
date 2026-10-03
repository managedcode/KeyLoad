using System.Security.Cryptography;
using KeyLoad.CrashHost;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class EpochUpgradeRecoveryAssertions
{
    private const int SourceDataEpoch = 5;
    private const int TargetDataEpoch = 6;
    private const string SystemNamespace = "system";
    private const string LastAppliedKey = "last-applied";
    private static readonly byte[] PostUpgradeKey = KeyCodec.Encode(EpochUpgradeFixture.Namespace, "post-upgrade");
    private static readonly byte[] PostUpgradeValue = [0xCA, 0xFE, 0xBA, 0xBE];

    internal static async Task VerifyAfterCrashAsync(string source, string target, CommitStage stage,
        EpochPriorProbeReceipt receipt, Dictionary<string, string> sourceInventory,
        CancellationToken cancellationToken)
    {
        await AssertSourceUnchangedAsync(source, receipt, sourceInventory, cancellationToken);
        EpochUpgradeFileInventory.AssertNativeHandlesReleased(source);
        var staging = target + ".upgrade";
        if (Directory.Exists(target))
        {
            await VerifyCurrentTargetAsync(target, receipt, cancellationToken);
            await Assert.That(Directory.Exists(staging)).IsFalse();
        }
        else
        {
            await Assert.That(File.Exists(target)).IsFalse();
            if (Directory.Exists(staging))
            {
                EpochUpgradeFileInventory.AssertNativeHandlesReleased(staging);
            }
        }

        if (stage >= CommitStage.UpgradeCheckpointFlushed)
        {
            await Assert.That(Directory.Exists(target) || Directory.Exists(staging)).IsTrue();
        }
    }

    internal static StoreIdentity UpgradeRetryAndVerify(string source, string target,
        EpochPriorProbeReceipt receipt)
    {
        var identity = ZoneTreeFormatUpgrade.Upgrade(source, new ZoneTreeStoreOptions(target));
        using var store = new ZoneTreeStore(new(target));
        AssertCurrentTarget(store, receipt);
        var snapshotPath = Path.Combine(Path.GetDirectoryName(target)!,
            "epoch-verify-" + Guid.NewGuid().ToString("N") + ".checkpoint");
        try
        {
            var created = store.CreateSnapshot(snapshotPath, receipt.AppliedPosition);
            var verified = store.VerifySnapshot(snapshotPath);
            if (created != verified || verified.Position != receipt.Position
                || verified.AppliedPosition != receipt.AppliedPosition)
            {
                throw new InvalidDataException("The upgraded current-format checkpoint failed its exact cut check.");
            }
        }
        finally
        {
            if (File.Exists(snapshotPath))
            {
                File.Delete(snapshotPath);
            }
        }
        return identity;
    }

    internal static StoreIdentity RetryPublishedTarget(string source, string target)
        => ZoneTreeFormatUpgrade.Upgrade(source, new(target));

    internal static async Task AssertIdentityAsync(StoreIdentity actual, EpochPriorProbeReceipt source)
    {
        await Assert.That(source.DataEpoch).IsEqualTo(SourceDataEpoch);
        await Assert.That(actual.FormatVersion).IsEqualTo(TargetDataEpoch);
        await Assert.That(actual.NodeId).IsEqualTo(source.NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(source.Incarnation);
        await Assert.That(actual.ReadGeneration).IsEqualTo(source.ReadGeneration);
        await Assert.That(actual.DispatchPaused).IsEqualTo(source.DispatchPaused);
        await Assert.That(Convert.ToHexStringLower(SHA256.HashData(actual.SigningKey.Span)))
            .IsEqualTo(source.SigningKeySha256);
        await Assert.That(actual.Durability).IsEqualTo(source.Durability);
    }

    internal static async Task<Dictionary<string, string>> WriteAndCaptureLaterTargetAsync(string target,
        EpochPriorProbeReceipt receipt, CancellationToken cancellationToken)
    {
        using (var store = new ZoneTreeStore(new(target)))
        {
            AssertCurrentTarget(store, receipt);
            var next = store.Commit((transaction, position) =>
            {
                transaction.Put(PostUpgradeKey, PostUpgradeValue);
                return position;
            });
            await Assert.That(next).IsEqualTo(receipt.Position + 1);
        }
        return await EpochUpgradeFileInventory.CaptureAsync(target, cancellationToken);
    }

    internal static async Task AssertLaterTargetWasPreservedAsync(string target, EpochPriorProbeReceipt receipt,
        Dictionary<string, string> expectedInventory, CancellationToken cancellationToken)
    {
        await EpochUpgradeFileInventory.AssertUnchangedAsync(target, expectedInventory, cancellationToken);
        using var store = new ZoneTreeStore(new(target));
        await Assert.That(store.Position).IsEqualTo(receipt.Position + 1);
        AssertCurrentRecords(store, receipt);
        await Assert.That(store.Read(view => view.ReadOwnedValue(PostUpgradeKey)))
            .IsEquivalentTo(PostUpgradeValue, CollectionOrdering.Matching);
        await AssertIdentityAsync(store.Identity, receipt);
    }

    private static async Task AssertSourceUnchangedAsync(string source, EpochPriorProbeReceipt receipt,
        Dictionary<string, string> expectedInventory, CancellationToken cancellationToken)
    {
        await EpochUpgradeFileInventory.AssertUnchangedAsync(source, expectedInventory, cancellationToken);
        await Assert.That(receipt.DataEpoch).IsEqualTo(SourceDataEpoch);
        await Assert.That(receipt.Position).IsEqualTo(1);
        await Assert.That(receipt.AppliedPosition).IsEqualTo(EpochUpgradeFixture.AppliedPosition);
    }

    private static async Task VerifyCurrentTargetAsync(string target, EpochPriorProbeReceipt receipt,
        CancellationToken cancellationToken)
    {
        var targetInventory = await EpochUpgradeFileInventory.CaptureAsync(target, cancellationToken);
        using (var store = new ZoneTreeStore(new(target)))
        {
            AssertCurrentTarget(store, receipt);
            await AssertIdentityAsync(store.Identity, receipt);
        }
        EpochUpgradeFileInventory.AssertNativeHandlesReleased(target);
        await EpochUpgradeFileInventory.AssertUnchangedAsync(target, targetInventory, cancellationToken);
    }

    private static void AssertCurrentTarget(ZoneTreeStore store, EpochPriorProbeReceipt receipt)
    {
        if (store.Identity.FormatVersion != TargetDataEpoch || store.Position != receipt.Position)
        {
            throw new InvalidDataException("The offline-upgrade target is not at the complete expected current cut.");
        }
        AssertCurrentRecords(store, receipt);
    }

    private static void AssertCurrentRecords(ZoneTreeStore store, EpochPriorProbeReceipt receipt)
    {
        foreach (var (key, expected) in EpochUpgradeFixture.RawRecords())
        {
            var actual = store.Read(view => view.ReadOwnedValue(key));
            if (actual is null || !CryptographicOperations.FixedTimeEquals(actual, expected))
            {
                throw new InvalidDataException("An upgraded raw source record is absent or changed.");
            }
        }

        var appliedKey = KeyCodec.Encode(SystemNamespace, LastAppliedKey);
        var applied = store.Read(view => view.ReadOwnedValue(appliedKey));
        if (applied is null || NativeSerialization.Deserialize<long>(applied) != receipt.AppliedPosition)
        {
            throw new InvalidDataException("The upgraded applied-position record changed.");
        }
    }
}
