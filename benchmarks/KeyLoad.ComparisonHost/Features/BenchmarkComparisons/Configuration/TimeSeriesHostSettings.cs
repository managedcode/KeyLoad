using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

[KeyLoad.ConfigurationBinding]
internal sealed record TimeSeriesHostSettings(Uri Endpoint, string AdminKey, string ConnectionString,
    string Image, string KeyLoadBuildIdentity, string OutputDirectory, string SourceRevision,
    ComparisonExecutionIdentity? Identity)
{
    private const string TimescaleConnectionSetting = "ConnectionStrings:benchmark-timescale";
    private const string TimescaleImageSetting = "Benchmarks:Images:Timescale";
    private const string KeyLoadBuildSetting = "Benchmarks:Images:KeyLoad";
    private const string UnrecordedRevision = "unrecorded";

    internal static IOptions<TimeSeriesHostSettings> Read(IConfiguration configuration)
        => ComparisonHostOptionsRegistration.Bind(() => ReadValidated(configuration));

    private static TimeSeriesHostSettings ReadValidated(IConfiguration configuration)
    {
        var endpoint = new Uri(ComparisonHostSettings.RequiredValue(configuration, ComparisonHostConstants.KeyLoadEndpoint), UriKind.Absolute);
        var adminKey = ComparisonHostSettings.RequiredValue(configuration, ComparisonHostConstants.AdminKey);
        var connection = ComparisonHostSettings.RequiredValue(configuration, TimescaleConnectionSetting);
        var image = ComparisonHostSettings.RequiredValue(configuration, TimescaleImageSetting);
        var build = ComparisonHostSettings.RequiredValue(configuration, KeyLoadBuildSetting);
        var output = Path.GetFullPath(ComparisonHostSettings.RequiredValue(configuration, ComparisonHostConstants.Output));
        var revision = configuration[ComparisonHostConstants.SourceRevision] ?? UnrecordedRevision;
        _ = ComparisonHostSettings.RequiredValue(configuration, ComparisonHostConstants.Storage);
        return new(endpoint, adminKey, connection, image, build, output, revision,
            ComparisonExecutionIdentity.ReadTimeSeries(configuration, revision));
    }

    public override string ToString() => nameof(TimeSeriesHostSettings);
}
