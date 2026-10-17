using System.Text;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

public sealed partial class SurrealDbTarget : IBenchmarkSessionQueries
{
    private const string SessionIndexSql = "DEFINE INDEX {0}_session ON TABLE {0} FIELDS key UNIQUE;";
    private const string SessionInfoSql = "INFO FOR TABLE {0};";
    private const string SessionReadSql = "SELECT key,payload FROM {0} WITH INDEX {0}_session WHERE key = {1} AND number >= {2} AND number < {3} ORDER BY key LIMIT 2;";
    private const string SessionIndexSuffix = "_session";
    private const string SessionFieldsClause = "FIELDS key";
    private const string SessionUniqueClause = "UNIQUE";
    private const string SessionIndexesProperty = "indexes";
    private const string SessionKeyProperty = "key";
    private const string SessionPayloadProperty = "payload";
    private const string SessionDefinitionMismatch = "SessionNativeIndexDefinitionMismatch";
    private const string SessionCardinalityMismatch = "SessionNativeQueryCardinalityMismatch";
    private const int SessionRangeWidth = 1;
    private const int SessionExpectedRows = 1;
    private const int SessionFirstRow = 0;
    private static readonly CompositeFormat SessionIndexFormat = CompositeFormat.Parse(SessionIndexSql);
    private static readonly CompositeFormat SessionInfoFormat = CompositeFormat.Parse(SessionInfoSql);
    private static readonly CompositeFormat SessionReadFormat = CompositeFormat.Parse(SessionReadSql);

    /// <inheritdoc/>
    public async Task BuildSessionIndexesAsync(CancellationToken cancellationToken)
    {
        await SurrealDbSqlTransport.ExecuteAsync(http, string.Format(System.Globalization.CultureInfo.InvariantCulture, SessionIndexFormat, table),
            Policy, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        using var response = await SurrealDbSqlTransport.QueryAsync(http, string.Format(System.Globalization.CultureInfo.InvariantCulture, SessionInfoFormat, table), Policy,
            cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        var definition = SurrealDbVectorProtocol.ReadRequiredString(SurrealDbVectorProtocol.SingleResult(response.RootElement)
            .GetProperty(SessionIndexesProperty), table + SessionIndexSuffix);
        if (!definition.Contains(SessionFieldsClause, StringComparison.OrdinalIgnoreCase)
            || !definition.Contains(SessionUniqueClause, StringComparison.OrdinalIgnoreCase))
        {
            throw new ComparisonFailureException(SessionDefinitionMismatch);
        }
    }

    /// <inheritdoc/>
    public async Task QuerySessionAsync(int ordinal, int stableStart, int stableCount, CancellationToken cancellationToken)
    {
        var expected = BenchmarkSessionQueryContract.Expected(ordinal, stableStart, stableCount);
        var sql = string.Format(System.Globalization.CultureInfo.InvariantCulture, SessionReadFormat, table,
            JsonSerializer.Serialize(expected.Id), expected.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            (expected.Number + SessionRangeWidth).ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var response = await SurrealDbSqlTransport.QueryAsync(http, sql, Policy,
            cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        var rows = SurrealDbVectorProtocol.SingleResult(response.RootElement);
        if (rows.GetArrayLength() != SessionExpectedRows)
        {
            throw new ComparisonFailureException(SessionCardinalityMismatch);
        }
        BenchmarkSessionQueryContract.Require(new(rows[SessionFirstRow].GetProperty(SessionKeyProperty).GetString()!, rows[SessionFirstRow].GetProperty(SessionPayloadProperty).GetString()!), expected);
    }
}
