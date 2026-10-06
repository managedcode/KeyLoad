using System.Collections.Immutable;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class NodeEpochReplicaSnapshotConversionTests
{
    private const string AdditionalSourceImage = "0123456789abcdef0123456789abcdef.snapshot";
    private const long FirstRetainedIndex = NodeEpochReplicaFixture.FirstRetainedEntryIndex;
    private const int RetainedEntryCount = NodeEpochReplicaFixture.RetainedSuffixEntryCount;
    private const int SnapshotMetadataCommits = 1;

    [Test]
    public async Task AcEpoch008RewritesCurrentSnapshotDescriptorAndPreservesOpaqueSourceInventory()
    {
        using var fixture = new NodeEpochReplicaFixture();
        fixture.AddSourceImage(AdditionalSourceImage);
        var originalPointer = fixture.Pointer;
        var originalReplicaPosition = fixture.ReplicaStore.Position;
        var originalCanonicalPosition = fixture.CanonicalStore.Position;
        ReplicaHardState originalHardState;
        (ImmutableArray<ReplicaEntry> Entries, byte[][] Bytes) originalSuffix;
        using (var originalLog = new DurableReplicaLog(fixture.ReplicaStore, UnitExecutionOptions.ReplicaConfiguration(fixture.Configuration), canonicalDatabase: fixture.Database))
        {
            originalHardState = originalLog.State;
            originalSuffix = await CaptureRetainedSuffixAsync(fixture, originalLog);
        }
        var originalSourceBytes = await File.ReadAllBytesAsync(Path.Combine(fixture.SourceSnapshots, originalPointer.FileName));
        var additionalSourceBytes = await File.ReadAllBytesAsync(Path.Combine(fixture.SourceSnapshots, AdditionalSourceImage));
        var plan = fixture.Preflight();
        await Assert.That(plan.Canonical.NodeId == plan.Replica.NodeId).IsFalse();
        await Assert.That(plan.Canonical.Position).IsEqualTo(fixture.CanonicalStore.Position);
        await Assert.That(plan.Replica.Position).IsEqualTo(originalReplicaPosition);

        ReplicaSnapshotFormatUpgrade.Upgrade(plan, fixture.Database, fixture.ReplicaStore, UnitExecutionOptions.ReplicaConfiguration(fixture.Configuration with { VoterIds = [.. fixture.Configuration.VoterIds] }), fixture.DestinationSnapshots, fixture.Convert, recoveryOptions: UnitExecutionOptions.OfflineRecovery(), executionOptions: UnitExecutionOptions.ReplicaExecution());

        using var log = new DurableReplicaLog(fixture.ReplicaStore, UnitExecutionOptions.ReplicaConfiguration(fixture.Configuration), canonicalDatabase: fixture.Database);
        var converted = log.State.Snapshot!;
        await Assert.That(fixture.ReplicaStore.Position).IsEqualTo(originalReplicaPosition + SnapshotMetadataCommits);
        await Assert.That(fixture.CanonicalStore.Position).IsEqualTo(originalCanonicalPosition);
        await Assert.That(ReplicaPersistence.AppliedPosition(fixture.CanonicalStore)).IsEqualTo(fixture.SourceCut.AppliedPosition);
        await Assert.That(log.State with { Snapshot = originalPointer }).IsEqualTo(originalHardState);
        await AssertRetainedSuffixPreservedAsync(fixture, log, originalSuffix);
        await Assert.That(converted.TransferId).IsEqualTo(originalPointer.TransferId);
        await Assert.That(converted.Incarnation).IsEqualTo(originalPointer.Incarnation);
        await Assert.That(converted.Index).IsEqualTo(originalPointer.Index);
        await Assert.That(converted.Term).IsEqualTo(originalPointer.Term);
        await Assert.That(converted.FileName).IsEqualTo(originalPointer.FileName);
        await Assert.That(converted.Length == originalPointer.Length && converted.Sha256 == originalPointer.Sha256).IsFalse();
        await Assert.That(await File.ReadAllBytesAsync(Path.Combine(fixture.SourceSnapshots, originalPointer.FileName)))
            .IsEquivalentTo(originalSourceBytes);
        await Assert.That(await File.ReadAllBytesAsync(Path.Combine(fixture.SourceSnapshots, AdditionalSourceImage)))
            .IsEquivalentTo(additionalSourceBytes);
        await Assert.That(Directory.EnumerateFiles(fixture.DestinationSnapshots).Select(path => Path.GetFileName(path)!))
            .IsEquivalentTo([originalPointer.FileName, AdditionalSourceImage]);
        new ReplicaSnapshotStore(fixture.CanonicalStore, log, UnitExecutionOptions.ReplicaConfiguration(fixture.Configuration), UnitExecutionOptions.ReplicaExecution()).Recover();
    }

    private static async Task<(ImmutableArray<ReplicaEntry> Entries, byte[][] Bytes)> CaptureRetainedSuffixAsync(
        NodeEpochReplicaFixture fixture, DurableReplicaLog log)
    {
        var entries = log.Read(FirstRetainedIndex, RetainedEntryCount, fixture.Configuration.MaxAppendBytes);
        await Assert.That(entries.Length).IsEqualTo(RetainedEntryCount);
        await Assert.That(entries.Select(entry => entry.Index))
            .IsEquivalentTo([FirstRetainedIndex, FirstRetainedIndex + 1]);
        var bytes = Enumerable.Range(0, RetainedEntryCount)
            .Select(offset => fixture.ReadRetainedEntryBytes(FirstRetainedIndex + offset)).ToArray();
        return (entries, bytes);
    }

    private static async Task AssertRetainedSuffixPreservedAsync(NodeEpochReplicaFixture fixture, DurableReplicaLog log,
        (ImmutableArray<ReplicaEntry> Entries, byte[][] Bytes) expected)
    {
        var entries = log.Read(FirstRetainedIndex, RetainedEntryCount, fixture.Configuration.MaxAppendBytes);
        await Assert.That(entries.Length).IsEqualTo(RetainedEntryCount);
        await Assert.That(entries.Select(entry => entry.Index))
            .IsEquivalentTo([FirstRetainedIndex, FirstRetainedIndex + 1]);
        await Assert.That(entries).IsEquivalentTo(expected.Entries);
        AssertEntryBytes(fixture, expected.Bytes);
    }

    private static void AssertEntryBytes(NodeEpochReplicaFixture fixture, byte[][] expected)
    {
        for (var offset = 0; offset < RetainedEntryCount; offset++)
        {
            var actual = fixture.ReadRetainedEntryBytes(FirstRetainedIndex + offset);
            if (!actual.AsSpan().SequenceEqual(expected[offset]))
            { throw new InvalidDataException($"Retained replica entry at index {FirstRetainedIndex + offset} changed."); }
        }
    }
}
