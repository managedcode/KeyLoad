using KeyLoad.Replication;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests;

/// <summary>AC-IS-004/005: actual snapshot files retain recovery and unsupported-format fault boundaries.</summary>
internal sealed class ReplicaNativeSnapshotTests
{
    private const string CanonicalDirectory = "canonical";
    private const string AppliedKey = "last-applied";
    private const string SystemKey = "system";
    private const string ValueKey = "value";
    private const string OldValue = "old-value";
    private const string NewValue = "new-value";
    private const int FirstChunkBytes = 32;
    private static readonly byte[] UnsupportedManifest = [0x00, 0xff, 0x4b, 0x4c];

    [Test]
    public async Task NativeIncomingDescriptorResumesAndUnsupportedDescriptorRefusesWithoutDeletingPartialImage()
    {
        using var sourceFiles = new ReplicaNativeFiles();
        using var targetFiles = new ReplicaNativeFiles(sourceFiles.Configuration.Incarnation, sourceFiles.SigningKey);
        using var sourceLogStore = sourceFiles.Open();
        using var sourceLog = new DurableReplicaLog(sourceLogStore, UnitExecutionOptions.ReplicaConfiguration(sourceFiles.Configuration));
        using var targetLogStore = targetFiles.Open();
        using var targetLog = new DurableReplicaLog(targetLogStore, UnitExecutionOptions.ReplicaConfiguration(targetFiles.Configuration));
        using var source = sourceFiles.Open(CanonicalDirectory);
        using var target = targetFiles.Open(CanonicalDirectory);
        var (sender, image) = CreateNativeSnapshot(source, sourceLog, sourceFiles.Configuration);
        target.Commit((tx, _) => { tx.PutRecord(KeyCodec.Encode(ValueKey), OldValue); return true; });
        var receiver = new ReplicaSnapshotStore(target, targetLog, UnitExecutionOptions.ReplicaConfiguration(targetFiles.Configuration), UnitExecutionOptions.ReplicaExecution());
        await Assert.That(receiver.Begin(image)).IsEqualTo(0);
        var first = sender.ReadChunk(image.TransferId, 0, FirstChunkBytes);
        await Assert.That(receiver.Append(image.TransferId, 0, first)).IsEqualTo(first.LongLength);
        var snapshotDirectory = Path.Combine(targetFiles.Configuration.Directory, ReplicaProtocol.SnapshotDirectory);
        var manifestPath = Path.Combine(snapshotDirectory, ReplicaProtocol.IncomingManifest);
        var imagePath = Path.Combine(snapshotDirectory, ReplicaProtocol.IncomingImage);
        var manifest = await File.ReadAllBytesAsync(manifestPath);
        await Assert.That(ReplicaProtocolCodec.Deserialize<ReplicaSnapshot>(manifest)).IsEqualTo(image);
        await VerifyUnsupportedDescriptorAsync(receiver, manifestPath, imagePath, first);
        await Assert.That(targetLog.State.CommittedIndex).IsEqualTo(0);
        await Assert.That(target.Read(view => view.GetRecord<string>(KeyCodec.Encode(ValueKey)))).IsEqualTo(OldValue);
        await File.WriteAllBytesAsync(manifestPath, manifest);
        await ResumeAndCompleteAsync(sender, image, target, targetLog, targetFiles.Configuration, first.LongLength);
        await Assert.That(target.Read(view => view.GetRecord<string>(KeyCodec.Encode(ValueKey)))).IsEqualTo(NewValue);
        await Assert.That(targetLog.State.CommittedIndex).IsEqualTo(1);
        await Assert.That(targetLog.State.Snapshot).IsEqualTo(image);
    }

    private static async Task ResumeAndCompleteAsync(ReplicaSnapshotStore sender, ReplicaSnapshot image,
        IAtomicStore target, IDurableReplicaLog targetLog, ReplicaConfiguration configuration, long expectedOffset)
    {
        var receiver = new ReplicaSnapshotStore(target, targetLog, UnitExecutionOptions.ReplicaConfiguration(configuration), UnitExecutionOptions.ReplicaExecution());
        receiver.Recover();
        var offset = receiver.Begin(image);
        await Assert.That(offset).IsEqualTo(expectedOffset);
        while (offset < image.Length)
        {
            offset = receiver.Append(image.TransferId, offset, sender.ReadChunk(image.TransferId, offset, configuration.SnapshotChunkBytes));
        }
        await Assert.That(receiver.Complete(image.TransferId)).IsEqualTo(image);
    }

    private static (ReplicaSnapshotStore Sender, ReplicaSnapshot Image) CreateNativeSnapshot(ZoneTreeStore source,
        DurableReplicaLog log, ReplicaConfiguration configuration)
    {
        log.SaveTermAndVote(1, null);
        log.Append([new(1, 1, null)]);
        log.Commit(1);
        source.Commit((tx, _) =>
        {
            tx.PutRecord(KeyCodec.Encode(SystemKey, AppliedKey), 1L);
            tx.PutRecord(KeyCodec.Encode(ValueKey), NewValue);
            return true;
        });
        var sender = new ReplicaSnapshotStore(source, log, UnitExecutionOptions.ReplicaConfiguration(configuration), UnitExecutionOptions.ReplicaExecution());
        return (sender, sender.Create(1, 1));
    }

    private static async Task VerifyUnsupportedDescriptorAsync(ReplicaSnapshotStore receiver,
        string manifestPath, string imagePath, byte[] first)
    {
        await File.WriteAllBytesAsync(manifestPath, UnsupportedManifest);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(receiver.Recover).Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(await File.ReadAllBytesAsync(manifestPath)).IsEquivalentTo(UnsupportedManifest, CollectionOrdering.Matching);
        await Assert.That(await File.ReadAllBytesAsync(imagePath)).IsEquivalentTo(first, CollectionOrdering.Matching);
        await Assert.That(receiver.Current).IsNull();
    }
}
