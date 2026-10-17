using System.Text;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Creates and observes the actual native scalar index used by ordered corpus readback.</summary>
internal static class SurrealDbReadbackIndex
{
    private const string NumberIndexSuffix = "_number";
    private const string Indexes = "indexes";
    private const string Fields = "FIELDS number";
    private const string Unique = "UNIQUE";
    private const string DefinitionFailure = "SurrealDbReadbackIndexDefinitionMismatch";
    private const string DefineTemplate = "DEFINE INDEX {0} ON TABLE {1} FIELDS number UNIQUE;";
    private const string InfoTemplate = "INFO FOR TABLE {0};";
    private static readonly CompositeFormat Define = CompositeFormat.Parse(DefineTemplate);
    private static readonly CompositeFormat Info = CompositeFormat.Parse(InfoTemplate);

    internal static string Name(string table) => table + NumberIndexSuffix;

    internal static async Task<string> CreateAsync(HttpClient http, string table, NativeComparisonExecutionOptions policy, TimeProvider timeProvider, CancellationToken token)
    {
        await SurrealDbSqlTransport.ExecuteAsync(http,
            string.Format(System.Globalization.CultureInfo.InvariantCulture, Define, Name(table), table), policy, cancellationToken: token, timeProvider: timeProvider).ConfigureAwait(false);
        using var response = await SurrealDbSqlTransport.QueryAsync(http,
            string.Format(System.Globalization.CultureInfo.InvariantCulture, Info, table), policy, cancellationToken: token, timeProvider: timeProvider).ConfigureAwait(false);
        var definition = SurrealDbVectorProtocol.ReadRequiredString(SurrealDbVectorProtocol.SingleResult(response.RootElement).GetProperty(Indexes), Name(table));
        if (!definition.Contains(Fields, StringComparison.OrdinalIgnoreCase) || !definition.Contains(Unique, StringComparison.OrdinalIgnoreCase))
        {
            throw new ComparisonFailureException(DefinitionFailure);
        }
        return definition;
    }
}
