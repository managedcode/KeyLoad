using System.Linq.Expressions;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class MetamorphicReferenceRequests
{
    internal static QueryRequest Sql(PartitionRef partition, int shape, bool descending, int offset)
        => new(partition, "SELECT * FROM " + MetamorphicReferenceData.Collection + " WHERE " + SqlPredicate(shape)
            + " ORDER BY bucket " + (descending ? "DESC" : "ASC") + ", id ASC OFFSET " + offset.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " ROWS FETCH NEXT 3 ROWS ONLY", Parameters(), AllowFullScan: true);
    private static string SqlPredicate(int shape) => shape switch
    {
        0 => "status = @wanted AND (score >= @minimum OR tag = @tag)",
        1 => "(status = @wanted AND score >= @minimum) OR (status = @wanted AND tag = @tag)",
        2 => "(tag = @tag OR score >= @minimum) AND status = @wanted",
        _ => "((status = @wanted AND score >= @minimum) OR (status = @wanted AND tag = @tag)) OR (status = @wanted AND score >= @minimum)"
    };
    internal static Dictionary<string, JsonElement> Parameters() => new(StringComparer.Ordinal)
    {
        ["wanted"] = JsonSerializer.SerializeToElement(MetamorphicReferenceData.Wanted, JsonDefaults.Options),
        ["minimum"] = JsonSerializer.SerializeToElement(MetamorphicReferenceData.Minimum, JsonDefaults.Options),
        ["tag"] = JsonSerializer.SerializeToElement(MetamorphicReferenceData.Tag, JsonDefaults.Options)
    };
    internal static AstQueryRequest Json(PartitionRef partition, int shape, bool descending, int offset)
    {
        Predicate p = new Comparison(new FieldOperand("/status"), "=", ValueOperand.Create(MetamorphicReferenceData.Wanted));
        Predicate q = new Comparison(new FieldOperand("/score"), ">=", ValueOperand.Create(MetamorphicReferenceData.Minimum));
        Predicate t = new Comparison(new FieldOperand("/tag"), "=", ValueOperand.Create(MetamorphicReferenceData.Tag));
        Predicate filter = shape switch
        {
            0 => new Logical(p, "AND", new Logical(q, "OR", t)),
            1 => new Logical(new Logical(p, "AND", q), "OR", new Logical(p, "AND", t)),
            2 => new Logical(new Logical(t, "OR", q), "AND", p),
            _ => new Logical(new Logical(new Logical(p, "AND", q), "OR", new Logical(p, "AND", t)), "OR", new Logical(p, "AND", q))
        };
        var request = new AstQueryRequest(partition, new(MetamorphicReferenceData.Collection, null, [new("*", "*")], filter,
            [new("/bucket", descending), new("/@id", false)], MetamorphicReferenceData.PageSize, Offset: offset), AllowFullScan: true);
        return JsonDefaults.Deserialize<AstQueryRequest>(JsonDefaults.Serialize(request));
    }
    internal static AstQueryRequest CSharp(PartitionRef partition, int shape, bool descending, int offset)
    {
        Expression<Func<MetamorphicReferenceRow, bool>> predicate = shape switch
        {
            0 => row => row.Status == MetamorphicReferenceData.Wanted && (row.Score >= MetamorphicReferenceData.Minimum || row.Tag == MetamorphicReferenceData.Tag),
            1 => row => row.Status == MetamorphicReferenceData.Wanted && row.Score >= MetamorphicReferenceData.Minimum || row.Status == MetamorphicReferenceData.Wanted && row.Tag == MetamorphicReferenceData.Tag,
            2 => row => (row.Tag == MetamorphicReferenceData.Tag || row.Score >= MetamorphicReferenceData.Minimum) && row.Status == MetamorphicReferenceData.Wanted,
            _ => row => row.Status == MetamorphicReferenceData.Wanted && row.Score >= MetamorphicReferenceData.Minimum || row.Status == MetamorphicReferenceData.Wanted && row.Tag == MetamorphicReferenceData.Tag || row.Status == MetamorphicReferenceData.Wanted && row.Score >= MetamorphicReferenceData.Minimum
        };
        var query = KeyLoadQuery.From<MetamorphicReferenceRow>(partition, MetamorphicReferenceData.Collection, UnitClientOptions.Translation()).Where(predicate);
        query = descending ? query.OrderByDescending(row => row.Bucket) : query.OrderBy(row => row.Bucket);
        var request = query.ThenBy(row => QueryFunctions.DocumentId(row)).Take(MetamorphicReferenceData.PageSize).ToRequest(true);
        return request with
        {
            Query = request.Query with
            {
                Offset = offset
            }
        };
    }
}
