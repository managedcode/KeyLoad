using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal static class CompositeSparseUniqueAssertions
{
    internal const string Collection = "sparse-composite";
    internal const string Index = "by-label-rank";
    internal const string Principal = "sparse-writer";
    internal const string NullId = "null-owner";
    internal const string MissingId = "missing-owner";
    internal const string NullDuplicate = "null-duplicate";
    internal const string MissingDuplicate = "missing-duplicate";
    internal const string HealthyId = "healthy-owner";
    internal const string NullJson = "{\"label\":\"shared\",\"rank\":null}";
    internal const string MissingJson = "{\"label\":\"shared\"}";
    internal const string SevenJson = "{\"label\":\"shared\",\"rank\":7}";
    internal const string EightJson = "{\"label\":\"shared\",\"rank\":8}";
    private const int MaximumRecords = 64;
    private static readonly string[] DomainFamilies = ["document", "index", "unique", "document-epoch", "outbox", "outbox-head"];

    internal static OperationResult Submit(TestDatabase db, params Mutation[] mutations)
        => DocumentCrudFixture.Submit(db, Principal, mutations);

    internal static async Task DocumentAsync(TestDatabase db, string id, string? json, long revision)
    {
        var document = db.Database.GetDocument(Principal, new(db.Partition, Collection, id));
        if (json is null)
        { await Assert.That(document).IsNull(); return; }
        await Assert.That(document).IsNotNull();
        var value = document!;
        await Assert.That(value.Reference).IsEqualTo(new EntityRef(db.Partition, Collection, id));
        await Assert.That(value.Json).IsEqualTo(json);
        await Assert.That(value.Revision).IsEqualTo(revision);
        await Assert.That(value.Redacted).IsFalse();
    }

    internal static async Task DuplicateAsync(TestDatabase db, string id, string json, bool included)
    {
        var before = CaptureDomain(db);
        var result = Submit(db, new PutDocument(Collection, id, json, 0));
        await Assert.That(result.Error).IsEqualTo(included ? ErrorCode.Conflict : (ErrorCode?)null);
        if (included)
        {
            await Assert.That(CaptureDomain(db).AsSpan().SequenceEqual(before)).IsTrue();
            await DocumentAsync(db, id, null, 0);
        }
        else
        { await DocumentAsync(db, id, json, 1); }
    }

    internal static async Task ImagesAsync(TestDatabase db, (string Id, object? Rank)[] rows)
    {
        foreach (var space in new[] { "index", "unique" })
        {
            var expected = rows.Select(row =>
            {
                object?[] parts = [Collection, Index, "shared", row.Rank];
                var key = KeySpace.Partition(space, db.Partition, space == "index" ? [.. parts, row.Id] : parts);
                return new KeyValuePair<string, string>(Convert.ToHexString(key), Convert.ToHexString(NativeSerialization.Serialize(row.Id)));
            }).OrderBy(row => row.Key, StringComparer.Ordinal).ToArray();
            var actual = db.Store.Read(view => Rows(view, KeySpace.Partition(space, db.Partition, Collection, Index)));
            await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        }
    }

    private static byte[] CaptureDomain(TestDatabase db) => db.Store.Read(view => JsonDefaults.Serialize(
        DomainFamilies.SelectMany(space => Rows(view, KeySpace.Partition(space, db.Partition))).ToArray()));

    private static KeyValuePair<string, string>[] Rows(IKeyValueView view, byte[] prefix)
    {
        var page = view.Scan(prefix, MaximumRecords);
        if (page.HasMore)
        { throw new InvalidOperationException("The complete sparse-index native domain exceeded its fixture bound."); }
        return page.Records.Select(row => new KeyValuePair<string, string>(Convert.ToHexString(row.Key.Span),
            Convert.ToHexString(row.Value.Span))).ToArray();
    }
}
