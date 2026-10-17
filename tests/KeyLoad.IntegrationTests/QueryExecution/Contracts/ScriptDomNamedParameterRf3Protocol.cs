namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ScriptDomNamedParameterRf3Protocol
{
    internal const string Count = "take";
    internal const string Collection = "collection";
    internal const string Stream = "stream";
    internal const string Generation = "generation";
    internal const string Id = "id";
    internal const string Title = "title";
    internal const string Amount = "amount";
    internal const string TypedId = "typed-row";
    internal const string TypedTitle = "literal";
    internal const string CountSql = "SELECT TOP (@take) title FROM offsetleft ORDER BY order_id";
    internal const string ModelSql = "SELECT TOP (10) payload.value AS value FROM EVENTS(@collection,@stream,@generation) ORDER BY revision";
    internal const string InsertSql = "INSERT INTO [parser-typed] (id,title,amount) VALUES (@id,@title,@amount)";
    internal const string InvalidQuery = "The SQL syntax or query arguments are invalid.";
    internal const string InvalidOperation = "The unified SQL statement or bound arguments are invalid.";
    internal const string SafeDetail = "The database operation could not be completed.";
    internal const string CompositionProperty = "CompositionReferences";
    internal const string EmptyProperty = "IsEmpty";
    internal const string Error = "error";
    internal const string Source = "source";
    internal const int Rows = 4;
    internal const int Routes = 4;
    internal const long FirstGeneration = 1L;
    internal const int TypedAmount = -1;
}
