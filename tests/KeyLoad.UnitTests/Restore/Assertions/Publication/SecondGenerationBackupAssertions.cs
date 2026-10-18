using System.Buffers.Binary;
using System.Text.Json;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class SecondGenerationBackupAssertions
{
    internal const string CurrentValue = "second-generation-current";
    private const string ErrorCodeKey = "errorCode";
    private const string RestoreCommand = "restore";

    internal static async Task LiteralAsync((StoreIdentity Identity, long Position, string[] Rows) actual)
    {
        string[] expected =
        [
            Convert.ToHexString(MetadataBackupFixture.StoredKeyBytes) + ":" + Convert.ToHexString(NativeSerialization.Serialize(CurrentValue)),
            Convert.ToHexString(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
                ZoneTreePersistenceFormat.LastAppliedKey)) + ":" + Convert.ToHexString(NativeSerialization.Serialize(1L)),
            Convert.ToHexString(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
                ZoneTreePersistenceFormat.ClockKey)) + ":" + Convert.ToHexString(NativeSerialization.Serialize(1L)),
            Convert.ToHexString(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace,
                ZoneTreePersistenceFormat.DispatchPausedKey)) + ":" + Convert.ToHexString(NativeSerialization.Serialize(true))
        ];
        await Assert.That(actual.Rows).IsEquivalentTo(expected.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
        await Assert.That(actual.Identity.DispatchPaused).IsTrue();
    }

    internal static async Task CheckpointAsync(Dictionary<string, byte[]> original, Dictionary<string, byte[]> current,
        StoreIdentity originalIdentity, StoreIdentity currentIdentity)
    {
        var before = Metadata(original[MetadataTestContract.JournalFileName]);
        var after = Metadata(current[MetadataTestContract.JournalFileName]);
        await Assert.That(before.Incarnation).IsEqualTo(originalIdentity.Incarnation);
        await Assert.That(after.Incarnation).IsEqualTo(originalIdentity.Incarnation);
        await Assert.That(after.Incarnation).IsNotEqualTo(currentIdentity.Incarnation);
        await BackupPersistedPolicyAssertions.SameAsync(before, after);
        var originalJournal = original[MetadataTestContract.JournalFileName];
        var currentJournal = current[MetadataTestContract.JournalFileName];
        var bytes = checked(ZoneTreePersistenceFormat.HeaderLength + BinaryPrimitives.ReadInt32LittleEndian(
            originalJournal.AsSpan(ZoneTreePersistenceFormat.PayloadLengthOffset)));
        await Assert.That(currentJournal.AsSpan(0, bytes).SequenceEqual(originalJournal.AsSpan(0, bytes))).IsTrue();
    }

    private static ZoneTreeCheckpointMetadata Metadata(byte[] journal)
    {
        if (journal.Length < ZoneTreePersistenceFormat.HeaderLength
            || BinaryPrimitives.ReadUInt64LittleEndian(journal) != ZoneTreePersistenceFormat.CheckpointMagic)
        {
            throw new InvalidOperationException("The original current-format archive has no checkpoint metadata.");
        }
        var length = BinaryPrimitives.ReadInt32LittleEndian(journal.AsSpan(ZoneTreePersistenceFormat.PayloadLengthOffset));
        if (length <= 0 || length > journal.Length - ZoneTreePersistenceFormat.HeaderLength)
        {
            throw new InvalidOperationException("The original checkpoint metadata exceeds its retained frame.");
        }
        return NativeSerialization.Deserialize<ZoneTreeCheckpointMetadata>(journal.AsSpan(
            ZoneTreePersistenceFormat.HeaderLength, length));
    }

    internal static async Task ConflictAsync(string archive, string target,
        (StoreIdentity Identity, long Position, string[] Rows) current,
        Dictionary<string, byte[]> captured, CancellationToken token)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => ZoneTreeStore.Restore(archive,
            target, UnitExecutionOptions.StorageExecution()));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Conflict);
        var result = await CliBackupRestoreProcess.RunAsync(CliBackupRestoreProcess.CaptureExecutionOptions(),
            [RestoreCommand, archive, target], token);
        await CliBackupRestoreAssertions.RejectedProcessAsync(result);
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.StandardOutput).IsEmpty();
        using var problem = JsonDocument.Parse(result.StandardError);
        await Assert.That(problem.RootElement.GetProperty(ErrorCodeKey).GetString()).IsEqualTo(nameof(ErrorCode.Conflict));
        await NativeBackupJournalRepairAssertions.SameCutAsync(current, NativeBackupJournalRepairAssertions.Read(target, token));
        await NativeBackupJournalRepairAssertions.SameArchiveAsync(captured, archive, token);
    }

    internal static async Task RestoredAsync((StoreIdentity Identity, long Position, string[] Rows) source,
        (StoreIdentity Identity, long Position, string[] Rows) actual)
    {
        await Assert.That(actual.Identity.NodeId).IsNotEqualTo(source.Identity.NodeId);
        await Assert.That(actual.Identity.Incarnation).IsNotEqualTo(source.Identity.Incarnation);
        await Assert.That(actual.Identity.SigningKey.Span.SequenceEqual(source.Identity.SigningKey.Span)).IsFalse();
        await Assert.That(actual.Identity.Durability).IsEqualTo(source.Identity.Durability);
        await Assert.That(actual.Position).IsEqualTo(source.Position + 1L);
        await LiteralAsync(actual);
    }
}
