using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Query;
using static KeyLoad.IntegrationTests.Features.QueryExecution.QueryEquivalentIntegerRf3DecimalProtocol;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryEquivalentIntegerRf3DecimalRequests
{
    internal static QueryRequest Sql(PartitionRef partition, QueryEquivalentIntegerRf3Kind kind, decimal minimum)
        => new(partition, SourcePrefix + Field(kind) + SourceTail, Parameters(minimum), AllowFullScan: true);
    private static Dictionary<string, JsonElement> Parameters(decimal minimum)
        => new(StringComparer.Ordinal)
        {
            [MinimumParameter] = JsonSerializer.SerializeToElement(minimum)
        };
    internal static AstQueryRequest Ast(PartitionRef partition, QueryEquivalentIntegerRf3Kind kind, decimal minimum)
        => RoundTrip(new(partition,
            new(Collection, null, [new(Star, Star)], new Comparison(new FieldOperand(PathPrefix + Field(kind)),
                Compare, new ParameterOperand(MinimumParameter)), [new(IdPath, false)], Limit),
            Parameters(minimum), AllowFullScan: true));
    internal static AstQueryRequest CSharp(PartitionRef partition, QueryEquivalentIntegerRf3Kind kind, decimal minimum)
    {
        var original = KeyLoadQuery.From<QueryEquivalentIntegerRf3DecimalRow>(partition, Collection, IntegrationClientOptions.Translation());
        var selected = kind switch
        {
            QueryEquivalentIntegerRf3Kind.Byte => original.Where(row => row.ByteNumber >= minimum),
            QueryEquivalentIntegerRf3Kind.SignedByte => original.Where(row => row.SignedByteNumber >= minimum),
            QueryEquivalentIntegerRf3Kind.Short => original.Where(row => row.ShortNumber >= minimum),
            QueryEquivalentIntegerRf3Kind.UnsignedShort => original.Where(row => row.UnsignedShortNumber >= minimum),
            QueryEquivalentIntegerRf3Kind.Int => original.Where(row => row.IntNumber >= minimum),
            QueryEquivalentIntegerRf3Kind.UnsignedInt => original.Where(row => row.UnsignedIntNumber >= minimum),
            QueryEquivalentIntegerRf3Kind.Long => original.Where(row => row.LongNumber >= minimum),
            QueryEquivalentIntegerRf3Kind.UnsignedLong => original.Where(row => row.UnsignedLongNumber >= minimum),
            _ => original.Where(row => checked((decimal)row.IntNumber) >= minimum)
        };
        return selected.OrderBy(row => QueryFunctions.DocumentId(row)).ToRequest(true);
    }
    private static AstQueryRequest RoundTrip(AstQueryRequest request)
        => JsonSerializer.Deserialize<AstQueryRequest>(JsonDefaults.Serialize(request), JsonDefaults.Options)!;
}
