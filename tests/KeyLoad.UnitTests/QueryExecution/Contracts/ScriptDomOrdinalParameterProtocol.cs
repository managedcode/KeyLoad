namespace KeyLoad.UnitTests.QueryExecution;

internal static class ScriptDomOrdinalParameterProtocol
{
    internal const string Count = "take";
    internal const string Collection = "collection";
    internal const string Stream = "stream";
    internal const string Generation = "generation";
    internal const string WindowSql = "SELECT TOP (@take) title FROM offsetleft ORDER BY order_id";
    internal const string ModelSql = "SELECT TOP (10) payload.value AS value FROM EVENTS(@collection,@stream,@generation) ORDER BY revision";
    internal const string InvalidQuery = "The SQL syntax or query arguments are invalid.";
    internal const string InvalidOperation = "The unified SQL version, statement or target operation is unsupported.";
    internal const string InvalidArguments = "The unified SQL statement or bound arguments are invalid.";
    internal const string GraphParameter = "A named SQL graph-path parameter is missing or has the wrong scalar type.";
    internal const int WindowRows = 4;
    internal const long FirstGeneration = 1;
}
