using System.Text.Json.Serialization;
using static KeyLoad.UnitTests.QueryExecution.QueryEquivalentIntegerDecimalProtocol;

namespace KeyLoad.UnitTests.QueryExecution;

internal readonly record struct QueryEquivalentIntegerDecimalRow(
    [property: JsonPropertyName(ByteField)] byte ByteNumber,
    [property: JsonPropertyName(SignedByteField)] sbyte SignedByteNumber,
    [property: JsonPropertyName(ShortField)] short ShortNumber,
    [property: JsonPropertyName(UnsignedShortField)] ushort UnsignedShortNumber,
    [property: JsonPropertyName(IntField)] int IntNumber,
    [property: JsonPropertyName(UnsignedIntField)] uint UnsignedIntNumber,
    [property: JsonPropertyName(LongField)] long LongNumber,
    [property: JsonPropertyName(UnsignedLongField)] ulong UnsignedLongNumber,
    decimal DecimalNumber, double DoubleNumber, int? OptionalNumber, QueryEquivalentApplicationNumber CustomNumber);
