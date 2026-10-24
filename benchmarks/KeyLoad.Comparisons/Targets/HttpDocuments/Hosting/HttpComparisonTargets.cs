using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Creates benchmark-only native ClickHouse and InfluxDB HTTP clients.</summary>
public static class HttpComparisonTargets
{
    private const string ClickHouse = "ClickHouse", InfluxDb = "InfluxDB";
    private const string Invalid = "HttpComparisonEngineRejected";

    /// <summary>Transfers the selected client's ownership to one isolated document comparison target.</summary>
    public static IComparisonTarget Create(string target, HttpClient http, string runId, string image,
        IOptions<ComparisonExecutionOptions> options)
    {
        _ = ComparisonExecutionOptions.Require(options);
        var name = PostgresSchemaIdentity.FromRunId(runId).Name;
        HttpDocumentStore store = target switch
        {
            ClickHouse => new ClickHouseDocumentStore(http, name, options),
            InfluxDb => new InfluxDocumentStore(http, name, options),
            _ => throw new InvalidOperationException(Invalid)
        };
        return new HttpDocumentTarget(http, store, image, options);
    }
}
