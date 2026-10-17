using System.Buffers.Binary;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.BackupRestore.Serialization;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class AtomicPartitionRosterFrameBudgetTests
{
    private const string RootPrincipalId = "root";
    private const string DocumentId = "roster-document";
    private const int NoFrameBytes = 0;
    private const int JournalFrameLimit = int.MaxValue;
    private const int DocumentPayloadBudgetDivisor = 4;
    private const int OneRosterMutation = 1;

    [Test]
    public async Task AcBackup004RosterFrameExhaustionPersistsRejectedOutcomeWithoutEffect()
    {
        using var calibration = new AtomicPartitionRosterFixture();
        calibration.ConfigureGraph();
        var documentJson = JsonSerializer.Serialize(new
        {
            payload = new string('x', calibration.Database.Limits.MaxDocumentBytes / DocumentPayloadBudgetDivisor)
        });
        var commandId = Guid.NewGuid();
        var successful = calibration.Batch(AtomicPartitionRosterFixture.Source, commandId,
            new PutDocument(AtomicPartitionRosterFixture.Collection, DocumentId, documentJson));
        await Assert.That(successful.Error).IsNull();
        var mutations = ReadLatestMutations(calibration.JournalPath);
        var rosterKey = AtomicPartitionRosterKeys.Partition(AtomicPartitionRosterFixture.Source);
        await Assert.That(mutations.Count(mutation => mutation.Key.Span.SequenceEqual(rosterKey))).IsEqualTo(OneRosterMutation);
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
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(persistedOutcome?.Result.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(bounded.ReadEntry(AtomicPartitionRosterFixture.Source)).IsNotNull();
        await Assert.That(bounded.Database.GetDocument(RootPrincipalId, new(AtomicPartitionRosterFixture.Source,
            AtomicPartitionRosterFixture.Collection, DocumentId))).IsNull();

        bounded.Reopen();
        var replay = bounded.Batch(AtomicPartitionRosterFixture.Source, commandId,
            new PutDocument(AtomicPartitionRosterFixture.Collection, DocumentId, documentJson));
        await Assert.That(replay.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(bounded.ReadEntry(AtomicPartitionRosterFixture.Source)).IsNotNull();
        await Assert.That(bounded.Database.GetDocument(RootPrincipalId, new(AtomicPartitionRosterFixture.Source,
            AtomicPartitionRosterFixture.Collection, DocumentId))).IsNull();
    }

    private static StorageMutation[] ReadLatestMutations(string journalPath)
    {
        var bytes = File.ReadAllBytes(journalPath);
        var offset = NoFrameBytes;
        StorageMutation[]? latest = null;
        while (offset < bytes.Length)
        {
            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(offset + ZoneTreePersistenceFormat.PayloadLengthOffset));
            var payloadStart = offset + ZoneTreePersistenceFormat.HeaderLength;
            latest = ZoneTreeJournalCodec.Deserialize(bytes.AsSpan(payloadStart, payloadLength).ToArray());
            offset = checked(payloadStart + payloadLength);
        }

        return latest ?? throw new InvalidDataException("The native journal contains no committed frame.");
    }

}
