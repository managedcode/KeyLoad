using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class ArtifactTransferFlowFixture
{
    private const int LargePayloadCharacters = 10_000;
    private const char PayloadCharacter = 'a';
    private const string LargePayloadKey = "artifact-flow/large";

    internal static byte[] LargePayloadKeyBytes => KeyCodec.Encode(LargePayloadKey);
    internal static string LargePayload => new(PayloadCharacter, LargePayloadCharacters);

    internal static void SeedAndCreateBackup(CliBackupRestoreFixture fixture)
    {
        using var store = new ZoneTreeStore(new(fixture.SourceDirectory),
            UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        store.Commit((transaction, _) =>
        {
            transaction.PutRecord(LargePayloadKeyBytes, LargePayload);
            return true;
        });
        store.Compact();
        store.CreateBackup(fixture.BackupDirectory);
    }
}
