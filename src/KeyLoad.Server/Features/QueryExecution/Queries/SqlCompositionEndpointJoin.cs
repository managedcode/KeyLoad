namespace KeyLoad.Server;

internal static class SqlCompositionEndpointJoin
{
    internal const int DialectVersion = 2;
    private const string As = "AS";
    private const string Inner = "INNER";
    private const string Join = "JOIN";
    private const string Collection = "COLLECTION";
    private const string On = "ON";
    private const string QueueAlias = "q";
    private const string FromAlias = "f";
    private const string ToAlias = "t";
    private const string FromEndpoint = "from";
    private const string ToEndpoint = "to";
    private const string EntityReference = "entity_ref";
    private const char PathSeparator = '.';
    private const char EqualsOperator = '=';

    internal static (string FromCollection, string ToCollection) Read(
        SqlOperationSyntaxReader reader, SqlCompositionParameters parameters)
    {
        Word(reader, As);
        Word(reader, QueueAlias);
        var from = Endpoint(reader, parameters, FromAlias, FromEndpoint);
        var to = Endpoint(reader, parameters, ToAlias, ToEndpoint);
        return (from, to);
    }

    private static string Endpoint(SqlOperationSyntaxReader reader,
        SqlCompositionParameters parameters, string alias, string endpoint)
    {
        Word(reader, Inner);
        Word(reader, Join);
        Word(reader, Collection);
        reader.Need(SqlOperationSyntax.OpenParenthesis);
        var collection = parameters.Read<string>();
        reader.Need(SqlOperationSyntax.CloseParenthesis);
        Word(reader, As);
        Word(reader, alias);
        Word(reader, On);
        Word(reader, QueueAlias);
        reader.Need(PathSeparator);
        Word(reader, endpoint);
        reader.Need(EqualsOperator);
        Word(reader, alias);
        reader.Need(PathSeparator);
        Word(reader, EntityReference);
        return collection;
    }

    private static void Word(SqlOperationSyntaxReader reader, string expected)
    {
        if (!reader.Identifier().Equals(expected, StringComparison.OrdinalIgnoreCase))
        { throw SqlOperationSyntax.UnsupportedInput(); }
    }
}
