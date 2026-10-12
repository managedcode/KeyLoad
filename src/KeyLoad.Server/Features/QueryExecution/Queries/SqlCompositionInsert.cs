namespace KeyLoad.Server;

/// <summary>Compiles the closed model INSERT grammar into the existing atomic command.</summary>
internal static class SqlCompositionInsert
{
    internal const string Statement = "INSERT";
    private const string Into = "INTO";
    private const string Select = "SELECT";
    private const string Links = "LINKS";
    private const string From = "FROM";
    private const string Limit = "LIMIT";
    private const string With = "WITH";
    private const string Command = "COMMAND";
    private const string GraphEdges = "GRAPH_EDGES";
    private const string QueueMessages = "QUEUE_MESSAGES";
    private const string GraphTraverse = "GRAPH_TRAVERSE";
    private const char Comma = ',';

    internal static CommandRequest Compile(SqlOperationRequest request, SqlOperationSyntaxReader reader)
    {
        if (request.AllowFullScan || request.Cursor is not null || request.Partition is null)
        { throw SqlOperationSyntax.InvalidInput(); }
        if (request.QueryDialectVersion != SqlOperationProtocol.DefaultQueryDialectVersion
            && request.QueryDialectVersion != SqlCompositionEndpointJoin.DialectVersion)
        { throw SqlOperationSyntax.UnsupportedInput(); }
        var parameters = new SqlCompositionParameters(request.Parameters, reader);
        Word(reader, Into);
        var target = reader.Identifier();
        Mutation mutation;
        if (target.Equals(GraphEdges, StringComparison.OrdinalIgnoreCase))
        { mutation = Forward(request, reader, parameters); }
        else if (target.Equals(QueueMessages, StringComparison.OrdinalIgnoreCase))
        {
            if (request.QueryDialectVersion != SqlOperationProtocol.DefaultQueryDialectVersion)
            { throw SqlOperationSyntax.UnsupportedInput(); }
            mutation = Reverse(reader, parameters);
        }
        else
        { throw SqlOperationSyntax.UnsupportedInput(); }
        Word(reader, With);
        Word(reader, Command);
        var id = parameters.Read<Guid>();
        if (id == Guid.Empty)
        { throw SqlOperationSyntax.InvalidInput(); }
        reader.Complete();
        parameters.Complete();
        return new(id, request.Partition, [mutation]);
    }

    private static QueueToGraph Forward(SqlOperationRequest request, SqlOperationSyntaxReader reader,
        SqlCompositionParameters parameters)
    {
        reader.Need(SqlOperationSyntax.OpenParenthesis);
        var graph = parameters.Read<string>();
        reader.Need(Comma);
        var prefix = parameters.Read<string>();
        reader.Need(SqlOperationSyntax.CloseParenthesis);
        Source(reader, QueueMessages);
        reader.Need(SqlOperationSyntax.OpenParenthesis);
        var queue = parameters.Read<string>();
        reader.Need(SqlOperationSyntax.CloseParenthesis);
        (string FromCollection, string ToCollection)? joined = request.QueryDialectVersion == SqlCompositionEndpointJoin.DialectVersion
            ? SqlCompositionEndpointJoin.Read(reader, parameters) : null;
        Word(reader, Limit);
        var maximum = parameters.Read<int>();
        return new(graph, queue, prefix, maximum, joined?.FromCollection, joined?.ToCollection);
    }

    private static GraphToQueueMutation Reverse(SqlOperationSyntaxReader reader, SqlCompositionParameters parameters)
    {
        reader.Need(SqlOperationSyntax.OpenParenthesis);
        var queue = parameters.Read<string>();
        reader.Need(Comma);
        var prefix = parameters.Read<string>();
        reader.Need(SqlOperationSyntax.CloseParenthesis);
        Source(reader, GraphTraverse);
        reader.Need(SqlOperationSyntax.OpenParenthesis);
        var graph = parameters.Read<string>();
        reader.Need(Comma);
        var start = parameters.Read<EntityRef>();
        reader.Need(Comma);
        var depth = parameters.Read<int>();
        reader.Need(Comma);
        var vertices = parameters.Read<int>();
        reader.Need(Comma);
        var edges = parameters.Read<int>();
        reader.Need(SqlOperationSyntax.CloseParenthesis);
        return new(queue, graph, start, prefix, depth, vertices, edges);
    }

    private static void Source(SqlOperationSyntaxReader reader, string source)
    {
        Word(reader, Select);
        Word(reader, Links);
        Word(reader, From);
        Word(reader, source);
    }

    private static void Word(SqlOperationSyntaxReader reader, string expected)
    {
        if (!reader.Identifier().Equals(expected, StringComparison.OrdinalIgnoreCase))
        { throw SqlOperationSyntax.UnsupportedInput(); }
    }
}
