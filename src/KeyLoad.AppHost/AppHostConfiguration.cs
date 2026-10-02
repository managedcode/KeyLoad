using Microsoft.Extensions.Configuration;

internal sealed record AppHostConfiguration(bool BenchmarkMode, string BenchmarkProfile, string BenchmarkRoot,
    string DataRoot, bool Ephemeral)
{
    private const string BenchmarkEnabled = "Benchmarks:Enabled";
    private const string BenchmarkProfileConfiguration = "Benchmarks:Profile";
    internal const string GeneralBenchmarkProfile = "general";
    internal const string TimeSeriesBenchmarkProfile = "timeseries";
    private const string BenchmarkDataRoot = "Benchmarks:DataRoot";
    private const string ClusterDataRoot = "KeyLoad:DataRoot";
    private const string EphemeralConfiguration = "KeyLoad:Ephemeral";
    private const string DefaultBenchmarkRoot = "../../artifacts/comparisons";
    private const string DefaultDataRoot = "../../data/cluster";
    private const string BenchmarkClusterDirectory = "cluster";
    private const string GuidFormat = "N";
    internal const string AdminParameter = "admin-key";

    internal static AppHostConfiguration Read(IDistributedApplicationBuilder builder)
    {
        var benchmark = builder.Configuration.GetValue<bool>(BenchmarkEnabled);
        var benchmarkProfile = (builder.Configuration[BenchmarkProfileConfiguration] ?? GeneralBenchmarkProfile).Trim();
        if (benchmark && !string.Equals(benchmarkProfile, GeneralBenchmarkProfile, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(benchmarkProfile, TimeSeriesBenchmarkProfile, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The configured benchmark profile is not supported.");
        }
        var benchmarkRoot = Path.GetFullPath(builder.Configuration[BenchmarkDataRoot]
            ?? Path.Combine(builder.AppHostDirectory, DefaultBenchmarkRoot, Guid.NewGuid().ToString(GuidFormat)));
        var dataRoot = Path.GetFullPath(builder.Configuration[ClusterDataRoot] ?? (benchmark
            ? Path.Combine(benchmarkRoot, BenchmarkClusterDirectory) : Path.Combine(builder.AppHostDirectory, DefaultDataRoot)));
        return new(benchmark, benchmarkProfile, benchmarkRoot, dataRoot,
            builder.Configuration.GetValue(EphemeralConfiguration, benchmark));
    }
}
