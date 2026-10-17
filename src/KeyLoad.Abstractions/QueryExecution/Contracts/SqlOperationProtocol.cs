namespace KeyLoad;

/// <summary>Stable unified SQL protocol values.</summary>
public static class SqlOperationProtocol
{
    /// <summary>First additive unified SQL envelope version.</summary>
    public const int Version = 1;
    /// <summary>Default query-language selector retained by existing callers.</summary>
    public const int DefaultQueryDialectVersion = 1;
    /// <summary>Conservatively admitted unified SQL route.</summary>
    public const string Route = "/v1/query/sql";
    /// <summary>Official MCP unified SQL adapter identity.</summary>
    public const string ToolName = "keyload_sql_execute";
    /// <summary>Procedure invocation keyword in the versioned unified SQL language.</summary>
    public const string CallKeyword = "CALL";
}
