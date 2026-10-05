using KeyLoad.AppHost.Hosting;

internal sealed record AppHostConfiguration(bool BenchmarkMode, string BenchmarkProfile, string BenchmarkRoot,
    string DataRoot, bool Ephemeral)
{
    internal const string GeneralBenchmarkProfile = "general";
    internal const string TimeSeriesBenchmarkProfile = "timeseries";
    private const string DefaultBenchmarkRoot = "../../artifacts/comparisons";
    private const string DefaultDataRoot = "../../data/cluster";
    private const string BenchmarkClusterDirectory = "cluster";
    private const string GuidFormat = "N";
    internal const string AdminParameter = "admin-key";

    internal static AppHostConfiguration Read(IDistributedApplicationBuilder builder)
    {
        const string MessageText = "The configured benchmark profile is not supported.";

        var options = AppHostOptionsRegistration.Get(builder).Startup.Value;
        var benchmark = options.BenchmarkMode;
        var benchmarkProfile = options.BenchmarkProfile.Trim();
        if (benchmark && !string.Equals(benchmarkProfile, GeneralBenchmarkProfile, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(benchmarkProfile, TimeSeriesBenchmarkProfile, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(MessageText);
        }
        var benchmarkRoot = Path.GetFullPath(options.BenchmarkRoot
            ?? Path.Combine(builder.AppHostDirectory, DefaultBenchmarkRoot, Guid.NewGuid().ToString(GuidFormat)));
        var dataRoot = Path.GetFullPath(options.DataRoot ?? (benchmark
            ? Path.Combine(benchmarkRoot, BenchmarkClusterDirectory) : Path.Combine(builder.AppHostDirectory, DefaultDataRoot)));
        return new(benchmark, benchmarkProfile, benchmarkRoot, dataRoot,
            options.Ephemeral ?? benchmark);
    }
}
