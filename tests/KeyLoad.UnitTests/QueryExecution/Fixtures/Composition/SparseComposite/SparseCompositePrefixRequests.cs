using KeyLoad.Client;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class SparseCompositePrefixRequests
{
    internal static QueryRequest Sql(PartitionRef partition, string collection, int shape, bool reference, bool consent = true)
    {
        var predicate = shape switch
        {
            1 => "status = 'open' AND rank = 7",
            2 => "status = 'open' AND rank = 99",
            _ => "status = 'open'"
        };
        if (reference)
        {
            predicate = "(" + predicate + ") OR (" + predicate + ")";
        }
        return new(partition, "SELECT TOP (10) * FROM " + collection + " WHERE " + predicate + " ORDER BY id",
            AllowFullScan: consent);
    }
    internal static AstQueryRequest Ast(PartitionRef partition, string collection, int shape, bool reference, bool consent = true)
    {
        Predicate predicate = new Comparison(new FieldOperand("/status"), "=", ValueOperand.Create("open"));
        if (shape != 0)
        {
            predicate = new Logical(predicate, "AND", new Comparison(new FieldOperand("/rank"), "=",
                ValueOperand.Create(shape == 1 ? 7m : 99m)));
        }
        return new(partition, new(collection, null, [new("*", "*")],
            reference ? new Logical(predicate, "OR", predicate) : predicate, [new("/@id", false)], 10),
            AllowFullScan: consent);
    }
    internal static AstQueryRequest CSharp(PartitionRef partition, string collection, int shape, bool reference, bool consent = true)
    {
        var query = KeyLoadQuery.From<SparseDocument>(partition, collection, UnitClientOptions.Translation());
        query = (shape, reference) switch
        {
            (1, false) => query.Where(row => row.Status == "open" && row.Rank == 7m),
            (1, true) => query.Where(row => row.Status == "open" && row.Rank == 7m || row.Status == "open" && row.Rank == 7m),
            (2, false) => query.Where(row => row.Status == "open" && row.Rank == 99m),
            (2, true) => query.Where(row => row.Status == "open" && row.Rank == 99m || row.Status == "open" && row.Rank == 99m),
            (0, true) => query.Where(row => row.Status == "open" || row.Status == "open"),
            _ => query.Where(row => row.Status == "open")
        };
        return query.OrderBy(row => QueryFunctions.DocumentId(row)).Take(10).ToRequest(consent);
    }
    private const string TypedSeedId = "c";
    internal static string SeedJson(string id, string literal)
        => id == TypedSeedId
            ? System.Text.Encoding.UTF8.GetString(JsonDefaults.Serialize(new SparseDocument("open", 7m)))
            : literal;

    private sealed record SparseDocument(string Status, decimal Rank);
}
