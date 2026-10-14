using System.Text.Json;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CurrentJournalSignatureConservation
{
    private const long CapturedPosition = 2L;
    private const long InitialAuthorityValue = 1L;

    internal static async Task RequireOriginalSourceAsync(NativeBackupCutFixture fixture,
        (StoreIdentity Identity, long Position, string[] Rows) source)
    {
        string[] expected =
        [
            Convert.ToHexString(MetadataBackupFixture.StoredKeyBytes) + ":" + Convert.ToHexString(NativeSerialization.Serialize(MetadataBackupFixture.ExpectedValue)),
            Convert.ToHexString(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace, ZoneTreePersistenceFormat.LastAppliedKey)) + ":" + Convert.ToHexString(NativeSerialization.Serialize(InitialAuthorityValue)),
            Convert.ToHexString(KeyCodec.Encode(ZoneTreePersistenceFormat.SystemNamespace, ZoneTreePersistenceFormat.ClockKey)) + ":" + Convert.ToHexString(NativeSerialization.Serialize(InitialAuthorityValue))
        ];
        await Assert.That(source.Position).IsEqualTo(CapturedPosition);
        await Assert.That(source.Rows).IsEquivalentTo(expected.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
        await Assert.That(JsonSerializer.Serialize(source.Identity, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(fixture.Identity, JsonDefaults.Options));
    }

    internal static async Task RunAsync(NativeBackupCutFixture fixture,
        (StoreIdentity Identity, long Position, string[] Rows) source, Dictionary<string, byte[]> archive,
        CancellationToken token)
    {
        await NativeBackupJournalRepairAssertions.SameCutAsync(source,
            NativeBackupJournalRepairAssertions.Read(fixture.Source, token));
        await NativeBackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.Backup, token);
    }

    internal static async Task RepairAsync(string backup, Dictionary<string, byte[]> original)
    {
        foreach (var row in original)
        { await File.WriteAllBytesAsync(Path.Combine(backup, row.Key), row.Value, CancellationToken.None); }
        await NativeBackupJournalRepairAssertions.SameArchiveAsync(original, backup, CancellationToken.None);
    }
}
