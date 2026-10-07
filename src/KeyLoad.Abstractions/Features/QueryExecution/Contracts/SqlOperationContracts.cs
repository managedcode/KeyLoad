using System.Text.Json;

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

/// <summary>Compiles one SQL SELECT or CALL into one existing authorized database operation.</summary>
/// <param name="Partition">Partition for Q1 SELECT; CALL binds scope through its canonical arguments.</param>
/// <param name="Sql">One admitted Q1/Q2 SELECT or Q1 CALL exact_public_operation(@arguments) statement.</param>
/// <param name="Parameters">Scalar SELECT parameters or one CALL argument-envelope object parameter.</param>
/// <param name="AllowFullScan">Explicit Q1 full-scan consent.</param>
/// <param name="Cursor">Optional Q1 continuation; unavailable for CALL.</param>
/// <param name="Version">Unified SQL envelope version.</param>
/// <param name="QueryDialectVersion">The explicit query-language selector; 1 preserves Q1.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SqlOperationRequest)]
public sealed record SqlOperationRequest([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Sql,
    [property: Orleans.Id(2)] Dictionary<string, JsonElement>? Parameters = null, [property: Orleans.Id(3)] bool AllowFullScan = false,
    [property: Orleans.Id(4)] string? Cursor = null, [property: Orleans.Id(5)] int Version = SqlOperationProtocol.Version,
    [property: Orleans.Id(6)] int QueryDialectVersion = SqlOperationProtocol.DefaultQueryDialectVersion);
