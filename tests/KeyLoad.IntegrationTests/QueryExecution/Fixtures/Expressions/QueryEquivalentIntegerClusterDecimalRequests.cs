using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Query;
using static KeyLoad.IntegrationTests.Features.QueryExecution.QueryEquivalentIntegerClusterDecimalProtocol;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryEquivalentIntegerClusterDecimalRequests
{
    internal static QueryRequest Sql(PartitionRef partition, QueryEquivalentIntegerClusterKind kind, decimal minimum)
        => new(partition, SourcePrefix + Field(kind) + SourceTail, Parameters(minimum), AllowFullScan: true);
    private static Dictionary<string, JsonElement> Parameters(decimal minimum)
        => new(StringComparer.Ordinal)
        {
            [MinimumParameter] = JsonSerializer.SerializeToElement(minimum)
        };
    internal static AstQueryRequest Ast(PartitionRef partition, QueryEquivalentIntegerClusterKind kind, decimal minimum)
        => RoundTrip(new(partition,
            new(Collection, null, [new(Star, Star)], new Comparison(new FieldOperand(PathPrefix + Field(kind)),
                Compare, new ParameterOperand(MinimumParameter)), [new(IdPath, false)], Limit),
            Parameters(minimum), AllowFullScan: true));
    internal static AstQueryRequest CSharp(PartitionRef partition, QueryEquivalentIntegerClusterKind kind, decimal minimum)
    {
        var original = KeyLoadQuery.From<QueryEquivalentIntegerClusterDecimalRow>(partition, Collection, IntegrationClientOptions.Translation());
        var selected = kind switch
        {
            QueryEquivalentIntegerClusterKind.Byte => original.Where(row => row.ByteNumber >= minimum),
            QueryEquivalentIntegerClusterKind.SignedByte => original.Where(row => row.SignedByteNumber >= minimum),
            QueryEquivalentIntegerClusterKind.Short => original.Where(row => row.ShortNumber >= minimum),
            QueryEquivalentIntegerClusterKind.UnsignedShort => original.Where(row => row.UnsignedShortNumber >= minimum),
            QueryEquivalentIntegerClusterKind.Int => original.Where(row => row.IntNumber >= minimum),
            QueryEquivalentIntegerClusterKind.UnsignedInt => original.Where(row => row.UnsignedIntNumber >= minimum),
            QueryEquivalentIntegerClusterKind.Long => original.Where(row => row.LongNumber >= minimum),
            QueryEquivalentIntegerClusterKind.UnsignedLong => original.Where(row => row.UnsignedLongNumber >= minimum),
            _ => original.Where(row => checked((decimal)row.IntNumber) >= minimum)
        };
        return selected.OrderBy(row => QueryFunctions.DocumentId(row)).ToRequest(true);
    }
    private static AstQueryRequest RoundTrip(AstQueryRequest request)
        => JsonSerializer.Deserialize<AstQueryRequest>(JsonDefaults.Serialize(request), JsonDefaults.Options)!;
}
