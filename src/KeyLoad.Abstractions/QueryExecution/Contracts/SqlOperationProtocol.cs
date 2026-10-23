namespace KeyLoad;

/// <summary>Stable unified SQL protocol values.</summary>
public static class SqlOperationProtocol
{
    /// <summary>Current unified SQL envelope format.</summary>
    public const int Version = 1;
    /// <summary>The single current query-language format.</summary>
    public const int DefaultQueryDialectVersion = 1;
    /// <summary>Conservatively admitted unified SQL route.</summary>
    public const string Route = "/query/sql";
    /// <summary>Official MCP unified SQL adapter identity.</summary>
    public const string ToolName = "keyload_sql_execute";
}
