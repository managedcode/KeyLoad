namespace KeyLoad.Comparisons.Targets;

internal static class PostgresNativeVectorStorageValues
{
    internal const int NumberColumnOrdinal = 0;
    internal const int IdColumnOrdinal = 1;
    internal const int EmbeddingColumnOrdinal = 2;
    internal const int PayloadIdColumnOrdinal = 3;
    internal const int PayloadNumberColumnOrdinal = 4;
    internal const int PaddingColumnOrdinal = 5;
    internal const int FirstIndex = 0;
    internal const int SingleElementOffset = 1;
    internal const string PostgreSQLDidNotUpdateExactlyOne = "PostgreSQL did not update exactly one vector row.";
    internal const int FloatTextCapacityEstimate = 14;
    internal const int VectorDelimiterCharacterCount = 2;
    internal const char VectorOpenCharacter = '[';
    internal const char ItemSeparatorCharacter = ',';
    internal const char VectorCloseCharacter = ']';
    internal const int VectorDimensions = 128;
    internal const int PayloadBytes = 1024;
    internal const string TheVectorDocumentViolatesItsFrozen = "The vector document violates its frozen dimension or payload length.";
    internal const int PayloadPropertyCount = 3;
    internal const int FieldCountColumnOrdinal = 6;
    internal const int IdTypeColumnOrdinal = 7;
    internal const int NumberTypeColumnOrdinal = 8;
    internal const int PaddingTypeColumnOrdinal = 9;
    internal const string PostgreSQLJSONBPayloadFieldsDifferFrom = "PostgreSQL JSONB payload fields differ from the native vector identity or contain unexpected properties.";
    internal const int IdCharacterCount = 10;
    internal const char VectorIdPrefixCharacter = 'v';
    internal const string CanonicalPayloadPrefix = "{\"id\":\"";
    internal const string CanonicalNumberPrefix = "\",\"number\":";
    internal const string CanonicalEmptyPaddingSuffix = ",\"padding\":\"\"}";
    internal const string PostgreSQLJSONBPayloadDoesNotMatch = "PostgreSQL JSONB payload does not match the frozen canonical shape or byte length.";
    internal const string CanonicalPaddingPrefix = ",\"padding\":\"";
    internal const string CanonicalPayloadSuffix = "\"}";
    internal const string PostgreSQLReturnedAnInvalidPgvectorValue = "PostgreSQL returned an invalid pgvector value.";
}
