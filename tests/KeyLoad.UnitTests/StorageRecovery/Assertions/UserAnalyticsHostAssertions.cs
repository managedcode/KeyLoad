using System.Security.Cryptography;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using P = KeyLoad.UnitTests.Features.StorageRecovery.UserAnalyticsHostProtocol;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class UserAnalyticsHostAssertions
{
    internal static UserAnalyticsHostCut Capture(PartitionHost host, ReplicatedOperation[] originals, CommitReceipt[] receipts)
        => new(host.Database.Store.Identity, host.Database.Store.Position,
            QueueWholeFlowStorage.Bytes((ZoneTreeStore)host.Database.Store), originals, receipts,
            Convert.ToHexString(host.Database.Store.Identity.SigningKey.Span));

    internal static async Task CutAsync(PartitionHost host, UserAnalyticsHostCut expected)
    {
        var identity = host.Database.Store.Identity;
        await Assert.That(identity.NodeId).IsEqualTo(expected.Identity.NodeId);
        await Assert.That(identity.Incarnation).IsEqualTo(expected.Identity.Incarnation);
        await Assert.That(identity.FormatVersion).IsEqualTo(expected.Identity.FormatVersion);
        await Assert.That(identity.KeyCodecVersion).IsEqualTo(expected.Identity.KeyCodecVersion);
        await Assert.That(identity.MinimumReaderContract).IsEqualTo(expected.Identity.MinimumReaderContract);
        await Assert.That(identity.Durability).IsEqualTo(expected.Identity.Durability);
        await Assert.That(identity.DispatchPaused).IsEqualTo(expected.Identity.DispatchPaused);
        await Assert.That(identity.ReadGeneration).IsGreaterThanOrEqualTo(expected.Identity.ReadGeneration);
        await Assert.That(CryptographicOperations.FixedTimeEquals(identity.SigningKey.Span,
            Convert.FromHexString(expected.SigningKey))).IsTrue();
        await Assert.That(host.Database.Store.Position).IsEqualTo(expected.Position);
        await Assert.That(QueueWholeFlowStorage.Bytes((ZoneTreeStore)host.Database.Store)
            .SequenceEqual(expected.Rows, StringComparer.Ordinal)).IsTrue();
    }

    internal static async Task EmptyAsync(PartitionHost host, UserAnalyticsRuntime runtime)
    {
        await Assert.That(runtime.CanReleasePhysical).IsTrue();
        var root = Path.Combine(host.DirectoryPath, P.DirectoryName);
        await Assert.That(!Directory.Exists(root) || !Directory.EnumerateFileSystemEntries(root).Any()).IsTrue();
    }

    internal static async Task SameAsync<T>(T expected, T actual)
        => await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(expected)));
}
