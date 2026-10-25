using System.Linq.Expressions;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Query;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class MetamorphicPublicRequests
{
    private const string WantedParameter = "wanted";
    private const string MinimumParameter = "minimum";
    private const string TagParameter = "tag";
    internal static QueryRequest Sql(PartitionRef partition, int shape, bool descending, int offset)
        => new(partition, "SELECT * FROM " + MetamorphicPublicData.Collection + " WHERE " + SqlPredicate(shape)
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
        [WantedParameter] = JsonSerializer.SerializeToElement(MetamorphicPublicData.Wanted, JsonDefaults.Options),
        [MinimumParameter] = JsonSerializer.SerializeToElement(MetamorphicPublicData.Minimum, JsonDefaults.Options),
        [TagParameter] = JsonSerializer.SerializeToElement(MetamorphicPublicData.Tag, JsonDefaults.Options)
    };
    internal static AstQueryRequest Json(PartitionRef partition, int shape, bool descending, int offset)
    {
        Predicate p = new Comparison(new FieldOperand("/status"), "=", ValueOperand.Create(MetamorphicPublicData.Wanted));
        Predicate q = new Comparison(new FieldOperand("/score"), ">=", ValueOperand.Create(MetamorphicPublicData.Minimum));
        Predicate t = new Comparison(new FieldOperand("/tag"), "=", ValueOperand.Create(MetamorphicPublicData.Tag));
        Predicate filter = shape switch
        {
            0 => new Logical(p, "AND", new Logical(q, "OR", t)),
            1 => new Logical(new Logical(p, "AND", q), "OR", new Logical(p, "AND", t)),
            2 => new Logical(new Logical(t, "OR", q), "AND", p),
            _ => new Logical(new Logical(new Logical(p, "AND", q), "OR", new Logical(p, "AND", t)), "OR", new Logical(p, "AND", q))
        };
        var request = new AstQueryRequest(partition, new(MetamorphicPublicData.Collection, null, [new("*", "*")], filter,
            [new("/bucket", descending), new("/@id", false)], MetamorphicPublicData.PageSize, Offset: offset), AllowFullScan: true);
        return JsonDefaults.Deserialize<AstQueryRequest>(JsonDefaults.Serialize(request));
    }
    internal static AstQueryRequest CSharp(PartitionRef partition, int shape, bool descending, int offset)
    {
        Expression<Func<MetamorphicPublicRow, bool>> predicate = shape switch
        {
            0 => row => row.Status == MetamorphicPublicData.Wanted && (row.Score >= MetamorphicPublicData.Minimum || row.Tag == MetamorphicPublicData.Tag),
            1 => row => row.Status == MetamorphicPublicData.Wanted && row.Score >= MetamorphicPublicData.Minimum || row.Status == MetamorphicPublicData.Wanted && row.Tag == MetamorphicPublicData.Tag,
            2 => row => (row.Tag == MetamorphicPublicData.Tag || row.Score >= MetamorphicPublicData.Minimum) && row.Status == MetamorphicPublicData.Wanted,
            _ => row => row.Status == MetamorphicPublicData.Wanted && row.Score >= MetamorphicPublicData.Minimum || row.Status == MetamorphicPublicData.Wanted && row.Tag == MetamorphicPublicData.Tag || row.Status == MetamorphicPublicData.Wanted && row.Score >= MetamorphicPublicData.Minimum
        };
        var query = KeyLoadQuery.From<MetamorphicPublicRow>(partition, MetamorphicPublicData.Collection, IntegrationClientOptions.Translation()).Where(predicate);
        query = descending ? query.OrderByDescending(row => row.Bucket) : query.OrderBy(row => row.Bucket);
        var request = query.ThenBy(row => QueryFunctions.DocumentId(row)).Take(MetamorphicPublicData.PageSize).ToRequest(true);
        return request with
        {
            Query = request.Query with
            {
                Offset = offset
            }
        };
    }
}
