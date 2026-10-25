using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class MetamorphicPublicData
{
    private const string StatusProperty = "status";
    private const string BucketProperty = "bucket";
    private const string TagProperty = "tag";
    private const string TitleProperty = "title";
    private const string ScoreProperty = "score";
    internal const string Collection = "metamorphic_rows";
    internal const string Index = "by-status";
    internal const string Wanted = "відкрито";
    internal const string Tag = "é";
    internal const decimal Minimum = 1m;
    internal const int Count = 48;
    internal const int PageSize = 3;
    internal const string ReadGrant = "status.read";
    internal const string UseGrant = "status.use";
    internal static ImmutableArray<MetamorphicPublicRow> Rows(int seed)
    {
        var result = ImmutableArray.CreateBuilder<MetamorphicPublicRow>();
        string[] tags = [Tag, "e\u0301", "🚀", "λ"];
        for (var index = 0; index < Count; index++)
        {
            var value = (index * 17 + seed) % Count;
            var id = "m" + index.ToString("D2", CultureInfo.InvariantCulture);
            var status = value % 3 == 0 ? "закрито" : Wanted;
            var missing = value % 5 == 0;
            var score = (Score(value)).Value;
            var body = new JsonObject
            {
                [StatusProperty] = status,
                [BucketProperty] = value % 4,
                [TagProperty] = tags[value % 4],
                [TitleProperty] = "PRIVATE-54 O'Brien /~ " + id
            };
            if (!missing)
            {
                body[ScoreProperty] = score is null ? null : JsonValue.Create(score.Value);
            }
            result.Add(new(id, status, score, missing, value % 4, tags[value % 4], body.ToJsonString()));
        }
        return result.ToImmutable();
    }
    private static global::ManagedCode.Communication.Result<decimal?> Score(int value) => value switch
    {
        var candidate when candidate % 5 is 0 or 1 => global::ManagedCode.Communication.Result<decimal?>.FailNotFound(nameof(Score)),
        47 => global::ManagedCode.Communication.Result<decimal?>.Succeed(((decimal?)(decimal.MaxValue))),
        42 => global::ManagedCode.Communication.Result<decimal?>.Succeed(((decimal?)(decimal.MinValue))),
        _ => global::ManagedCode.Communication.Result<decimal?>.Succeed(((decimal?)(value % 7 - 3)))
    };
    internal static ImmutableArray<MetamorphicPublicRow> Reference(ImmutableArray<MetamorphicPublicRow> rows, bool descending, int offset)
    {
        var matches = rows.Where(row => string.Equals(row.Status, Wanted, StringComparison.Ordinal)
            && (row.Score is { } score && score >= Minimum || string.Equals(row.Tag, Tag, StringComparison.Ordinal)));
        var ordered = descending ? matches.OrderByDescending(row => row.Bucket) : matches.OrderBy(row => row.Bucket);
        return [.. ordered.ThenBy(row => row.Id, StringComparer.Ordinal).Skip(offset)];
    }
    internal static QueryRow Expected(MetamorphicPublicRow row)
    {
        var body = new JsonObject { [StatusProperty] = row.Status, [BucketProperty] = row.Bucket, [TagProperty] = row.Tag };
        if (!row.Missing)
        {
            body[ScoreProperty] = row.Score is null ? null : JsonValue.Create(row.Score.Value);
        }
        return new(row.Id, 1L, body.ToJsonString(), true, ["/title"]);
    }
    internal static string Path(int shape) => shape is 0 or 2 ? "index:" + Index : "bounded-full-scan";
}
