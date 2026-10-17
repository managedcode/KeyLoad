using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Query;
using static KeyLoad.UnitTests.Features.QueryExecution.QueryEquivalentIntegerDecimalProtocol;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryEquivalentIntegerDecimalRequests
{
    internal static QueryRequest Sql(PartitionRef partition, QueryEquivalentIntegerKind kind, decimal minimum)
        => new(partition, SourcePrefix + Field(kind) + SourceTail, Parameters(minimum), AllowFullScan: true);
    private static Dictionary<string, JsonElement> Parameters(decimal minimum)
        => new(StringComparer.Ordinal)
        {
            [MinimumParameter] = JsonSerializer.SerializeToElement(minimum)
        };
    internal static AstQueryRequest Ast(PartitionRef partition, QueryEquivalentIntegerKind kind, decimal minimum)
        => Q2CapabilityFixtures.JsonRoundTrip(new(partition,
            new(Collection, null, [new(Star, Star)], new Comparison(new FieldOperand(PathPrefix + Field(kind)),
                Compare, new ParameterOperand(MinimumParameter)), [new(IdPath, false)], Limit),
            Parameters(minimum), AllowFullScan: true));
    internal static AstQueryRequest CSharp(PartitionRef partition, QueryEquivalentIntegerKind kind, decimal minimum)
    {
        var original = KeyLoadQuery.From<QueryEquivalentIntegerDecimalRow>(partition, Collection, UnitClientOptions.Translation());
        var selected = kind switch
        {
            QueryEquivalentIntegerKind.Byte => original.Where(row => row.ByteNumber >= minimum),
            QueryEquivalentIntegerKind.SignedByte => original.Where(row => row.SignedByteNumber >= minimum),
            QueryEquivalentIntegerKind.Short => original.Where(row => row.ShortNumber >= minimum),
            QueryEquivalentIntegerKind.UnsignedShort => original.Where(row => row.UnsignedShortNumber >= minimum),
            QueryEquivalentIntegerKind.Int => original.Where(row => row.IntNumber >= minimum),
            QueryEquivalentIntegerKind.UnsignedInt => original.Where(row => row.UnsignedIntNumber >= minimum),
            QueryEquivalentIntegerKind.Long => original.Where(row => row.LongNumber >= minimum),
            QueryEquivalentIntegerKind.UnsignedLong => original.Where(row => row.UnsignedLongNumber >= minimum),
            _ => original.Where(row => checked((decimal)row.IntNumber) >= minimum)
        };
        return selected.OrderBy(row => QueryFunctions.DocumentId(row)).ToRequest(true);
    }
    internal static Action[] UnsupportedCalls(PartitionRef partition)
    {
        var query = KeyLoadQuery.From<QueryEquivalentIntegerDecimalRow>(partition, Collection, UnitClientOptions.Translation());
        return [() => query.Where(row => (int)row.LongNumber >= int.MinValue),
            () => query.Where(row => (short)row.IntNumber >= short.MinValue),
            () => query.Where(row => checked((short)row.IntNumber) >= short.MinValue),
            () => query.Where(row => (int)row.DecimalNumber >= int.MinValue),
            () => query.Where(row => (decimal)row.DoubleNumber >= Threshold),
            () => query.Where(row => row.OptionalNumber >= Threshold),
            () => query.Where(row => row.CustomNumber >= Threshold)];
    }
}
