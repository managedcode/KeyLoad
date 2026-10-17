using System.Text.Json.Serialization;
using static KeyLoad.IntegrationTests.Features.QueryExecution.QueryEquivalentIntegerRf3DecimalProtocol;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal readonly record struct QueryEquivalentIntegerRf3DecimalRow(
    [property: JsonPropertyName(ByteField)] byte ByteNumber,
    [property: JsonPropertyName(SignedByteField)] sbyte SignedByteNumber,
    [property: JsonPropertyName(ShortField)] short ShortNumber,
    [property: JsonPropertyName(UnsignedShortField)] ushort UnsignedShortNumber,
    [property: JsonPropertyName(IntField)] int IntNumber,
    [property: JsonPropertyName(UnsignedIntField)] uint UnsignedIntNumber,
    [property: JsonPropertyName(LongField)] long LongNumber,
    [property: JsonPropertyName(UnsignedLongField)] ulong UnsignedLongNumber);
