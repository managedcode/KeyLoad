using System.Text.Json;
using KeyLoad.Query;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresDecimalRf3Protocol
{
    internal const string Parameter = "p1";
    internal const string Sql = "SELECT label AS label FROM null_ordering WHERE score >= @p1 ORDER BY label ASC LIMIT 5";
    internal const string DriverSql = "SELECT label AS label FROM null_ordering WHERE score >= $1 ORDER BY label ASC LIMIT 5";
    internal const string Statement = "numeric_statement";
    internal const string Portal = "numeric_portal";
    internal const string Terminator = "\0";
    internal const int NumericOid = 1700;
    internal const short One = 1;
    internal const short Text = 0;
    internal const short Binary = 1;
    internal const int HeaderBytes = sizeof(short) * 4;
    internal const ushort Positive = 0x0000;
    internal const ushort NaN = 0xC000;
    internal const ushort Negative = 0x4000;
    internal const string Unsupported = "SERROR\0C0A000\0MUnsupportedCapability\0\0";
    internal const int First = 0;
    internal const decimal HighScore = 2m;
    internal const decimal BetweenScores = 1.5m;
    internal const string HighId = "high";
    internal const string LowId = "low";
    internal const string HighJson = """{"label":"high"}""";
    internal const string LowJson = """{"label":"low"}""";
    internal static readonly decimal[] Thresholds =
        [decimal.MinValue, -PostgresDecimalRf3Protocol.BetweenScores, decimal.Zero, decimal.One, PostgresDecimalRf3Protocol.BetweenScores, 2m, decimal.MaxValue, 0.0000000000000000000000000001m,
         1.0000000000000000000000000001m, 2.000000000000000000000000001m];
    internal static Dictionary<string, JsonElement> Parameters(decimal value)
        => new() { [Parameter] = JsonSerializer.SerializeToElement(value, JsonDefaults.Options) };
    internal static SqlOperationRequest Operation(decimal value)
        => new(PostgresRf3State.Partition, Sql, Parameters(value), AllowFullScan: true);
    internal static AstQueryRequest Ast(decimal value)
        => new(PostgresRf3State.Partition, new(SqlNullOrderingRf3Protocol.Collection, null,
            [new(SqlNullOrderingRf3Protocol.LabelPath, SqlNullOrderingRf3Protocol.Label)],
            new Comparison(new FieldOperand(SqlNullOrderingRf3Protocol.ScorePath), GreaterOrEqual, new ParameterOperand(Parameter)),
            [new(SqlNullOrderingRf3Protocol.LabelPath, false)], SqlNullOrderingRf3Protocol.RowCount), Parameters(value), AllowFullScan: true);
    private const string GreaterOrEqual = ">=";
}
