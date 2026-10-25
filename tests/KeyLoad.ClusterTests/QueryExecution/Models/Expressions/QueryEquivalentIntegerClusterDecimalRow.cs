using System.Text.Json.Serialization;
using static KeyLoad.ClusterTests.QueryExecution.QueryEquivalentIntegerClusterDecimalProtocol;

namespace KeyLoad.ClusterTests.QueryExecution;

internal readonly record struct QueryEquivalentIntegerClusterDecimalRow(
    [property: JsonPropertyName(ByteField)] byte ByteNumber,
    [property: JsonPropertyName(SignedByteField)] sbyte SignedByteNumber,
    [property: JsonPropertyName(ShortField)] short ShortNumber,
    [property: JsonPropertyName(UnsignedShortField)] ushort UnsignedShortNumber,
    [property: JsonPropertyName(IntField)] int IntNumber,
    [property: JsonPropertyName(UnsignedIntField)] uint UnsignedIntNumber,
    [property: JsonPropertyName(LongField)] long LongNumber,
    [property: JsonPropertyName(UnsignedLongField)] ulong UnsignedLongNumber);
