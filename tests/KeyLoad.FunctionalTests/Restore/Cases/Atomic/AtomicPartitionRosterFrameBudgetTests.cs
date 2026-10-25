using System.Buffers.Binary;
using System.Text.Json;
using KeyLoad.Core.BackupRestore.Serialization;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal sealed class AtomicPartitionRosterFrameBudgetTests
{
    private const string RootPrincipalId = "root";
    private const string DocumentId = "roster-document";
    private const int JournalFrameLimit = int.MaxValue;
    private const int DocumentPayloadBytes = 262_144;

    [Test]
    public async Task AcBackup004RosterFrameExhaustionPersistsRejectedOutcomeWithoutEffect()
    {
        using var calibration = new AtomicPartitionRosterFixture();
        calibration.ConfigureGraph();
        var documentJson = JsonSerializer.Serialize(new
        {
            payload = new string('x', DocumentPayloadBytes)
        });
        var commandId = Guid.NewGuid();
        var successful = calibration.Batch(AtomicPartitionRosterFixture.Source, commandId,
            new PutDocument(AtomicPartitionRosterFixture.Collection, DocumentId, documentJson));
        await Assert.That(Errors.Code(successful.Problem).Value).IsNull();
        var mutations = ReadLatestMutations(calibration.JournalPath);
        var rosterKey = AtomicPartitionRosterKeys.Partition(AtomicPartitionRosterFixture.Source);
        await Assert.That(mutations.Count(mutation => mutation.Key.Span.SequenceEqual(rosterKey))).IsEqualTo(1);
        var withoutRoster = mutations.Where(mutation => !mutation.Key.Span.SequenceEqual(rosterKey)).ToArray();
        var frameLimit = ZoneTreeJournalCodec.Serialize(withoutRoster, JournalFrameLimit).Length;
        var completeLength = ZoneTreeJournalCodec.Serialize(mutations, JournalFrameLimit).Length;
        await Assert.That(completeLength).IsGreaterThan(frameLimit);

        using var bounded = new AtomicPartitionRosterFixture(configuredMaxFrameBytes: frameLimit);
        bounded.ConfigureGraph();
        var rejected = bounded.Batch(AtomicPartitionRosterFixture.Source, commandId,
            new PutDocument(AtomicPartitionRosterFixture.Collection, DocumentId, documentJson));
        var persistedOutcome = bounded.Store.Read(view => view.GetRecord<StoredOutcome>(
            KeySpace.PartitionOutcome(AtomicPartitionRosterFixture.Source, RootPrincipalId, commandId)));
        await Assert.That(Errors.Code(rejected.Problem).Value).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(Errors.Code(persistedOutcome?.Result.Problem).Value).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(bounded.ReadEntry(AtomicPartitionRosterFixture.Source)).IsNotNull();
        await Assert.That(bounded.Database.GetDocument(RootPrincipalId, new(AtomicPartitionRosterFixture.Source,
            AtomicPartitionRosterFixture.Collection, DocumentId))).IsNull();

        bounded.Reopen();
        var replay = bounded.Batch(AtomicPartitionRosterFixture.Source, commandId,
            new PutDocument(AtomicPartitionRosterFixture.Collection, DocumentId, documentJson));
        await Assert.That(Errors.Code(replay.Problem).Value).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(bounded.ReadEntry(AtomicPartitionRosterFixture.Source)).IsNotNull();
        await Assert.That(bounded.Database.GetDocument(RootPrincipalId, new(AtomicPartitionRosterFixture.Source,
            AtomicPartitionRosterFixture.Collection, DocumentId))).IsNull();
    }

    private static StorageMutation[] ReadLatestMutations(string journalPath)
    {
        var bytes = File.ReadAllBytes(journalPath);
        var offset = 0;
        StorageMutation[]? latest = null;
        while (offset < bytes.Length)
        {
            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(offset + ZoneTreePersistenceFormat.PayloadLengthOffset));
            var payloadStart = offset + ZoneTreePersistenceFormat.HeaderLength;
            latest = ZoneTreeJournalCodec.Deserialize(bytes.AsSpan(payloadStart, payloadLength).ToArray());
            offset = checked(payloadStart + payloadLength);
        }

        return latest ?? throw new InvalidDataException("The execution journal contains no committed frame.");
    }

}
