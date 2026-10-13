namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CatalogRestoreCanonicalCutArchive
{
    private const string CatalogFile = "catalog-backup.native";
    private const string InvalidDigest = "0000000000000000000000000000000000000000000000000000000000000000";

    internal static async Task ReplaceDigestAsync(string backup, CancellationToken token)
    {
        var path = Path.Combine(backup, CatalogFile);
        var bytes = await File.ReadAllBytesAsync(path, token);
        var envelope = global::KeyLoad.NativeSerialization.Deserialize<
            global::KeyLoad.Storage.ZoneTree.ZoneTreeCatalogBackupMetadata>(bytes);
        var cut = global::KeyLoad.NativeSerialization.Deserialize<global::KeyLoad.ClusterBackupOwnerCut>(
            envelope.Metadata.Span);
        await Assert.That(cut.CanonicalDigest).IsNotEqualTo(InvalidDigest);
        var invalid = cut with { CanonicalDigest = InvalidDigest };
        var replacement = envelope with { Metadata = global::KeyLoad.NativeSerialization.Serialize(invalid) };
        await File.WriteAllBytesAsync(path, global::KeyLoad.NativeSerialization.Serialize(replacement), token);
    }

    internal static Task RepairAsync(string backup, IReadOnlyDictionary<string, byte[]> archive,
        CancellationToken token)
        => File.WriteAllBytesAsync(Path.Combine(backup, CatalogFile), archive[CatalogFile], token);
}
