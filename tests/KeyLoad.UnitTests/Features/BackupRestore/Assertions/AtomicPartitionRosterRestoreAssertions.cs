using System.Security.Cryptography;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class AtomicPartitionRosterRestoreAssertions
{
    private const int OneRecord = 1;
    private const int FirstRecord = 0;
    private const long FirstRevision = 1;

    internal static string CanonicalDigest(ZoneTreeStore store) => store.Read(view =>
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[]? after = null;
        while (true)
        {
            var page = view.Scan([], OneRecord, after);
            if (page.Records.IsEmpty)
            { return Convert.ToHexString(digest.GetHashAndReset()); }
            var row = page.Records[FirstRecord];
            digest.AppendData(BitConverter.GetBytes(row.Key.Length));
            digest.AppendData(row.Key.Span);
            digest.AppendData(BitConverter.GetBytes(row.Value.Length));
            digest.AppendData(row.Value.Span);
            after = row.Key.ToArray();
            if (!page.HasMore)
            { return Convert.ToHexString(digest.GetHashAndReset()); }
        }
    });

    internal static async Task LiteralDocumentAsync(AtomicPartitionRosterRestoreFixture fixture, string id)
    {
        var reference = new EntityRef(AtomicPartitionRosterFixture.Source, AtomicPartitionRosterFixture.Collection, id);
        var actual = fixture.Database.GetDocument(AtomicPartitionRosterRestoreFixture.Principal, reference);
        var expected = new DocumentResult(reference, FirstRevision, AtomicPartitionRosterRestoreFixture.LiteralJson, false, []);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(fixture.ReadRoster().AsSpan().SequenceEqual(fixture.OriginalRoster)).IsTrue();
    }
}
