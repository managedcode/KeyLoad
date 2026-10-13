using System.Text.Json;
using KeyLoad.Query;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresInt16Rf3Protocol
{
    internal const string Parameter = "p1";
    internal const string Sql = "SELECT label AS label FROM null_ordering WHERE score >= @p1 ORDER BY label ASC LIMIT 5";
    internal const string DriverSql = "SELECT label AS label FROM null_ordering WHERE score >= $1 ORDER BY label ASC LIMIT 5";
    internal const string Statement = "int16_statement";
    internal const string Portal = "int16_portal";
    internal const string Terminator = "\0";
    internal const int Int16Oid = 21;
    internal const short One = 1;
    internal const short Text = 0;
    internal const short Binary = 1;
    internal const short OneValue = 1;
    internal const short HighScore = 2;
    internal const short ProbeValue = 2;
    internal const string HighId = "high";
    internal const string LowId = "low";
    internal const string HighJson = """{"label":"high"}""";
    internal const string LowJson = """{"label":"low"}""";
    internal static readonly short[] Thresholds = [short.MinValue, -1, 0, OneValue, HighScore, short.MaxValue];
    internal static Dictionary<string, JsonElement> Parameters(short value)
        => new() { [Parameter] = JsonSerializer.SerializeToElement(value, JsonDefaults.Options) };
    internal static SqlOperationRequest Operation(short value)
        => new(PostgresRf3State.Partition, Sql, Parameters(value), AllowFullScan: true);
    internal static AstQueryRequest Ast(short value)
        => new(PostgresRf3State.Partition, new(SqlNullOrderingRf3Protocol.Collection, null,
            [new(SqlNullOrderingRf3Protocol.LabelPath, SqlNullOrderingRf3Protocol.Label)],
            new Comparison(new FieldOperand(SqlNullOrderingRf3Protocol.ScorePath), GreaterOrEqual, new ParameterOperand(Parameter)),
            [new(SqlNullOrderingRf3Protocol.LabelPath, false)], SqlNullOrderingRf3Protocol.RowCount), Parameters(value), AllowFullScan: true);
    private const string GreaterOrEqual = ">=";
}
