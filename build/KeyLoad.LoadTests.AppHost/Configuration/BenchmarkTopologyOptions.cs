using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

/// <summary>Selects the one database, node count and KeyLoad image a benchmark job deploys.</summary>
[ConfigurationOptions]
internal sealed class BenchmarkTopologyOptions
{
    internal const string Section = "Benchmarks";
    internal const string Invalid = "Set Benchmarks:Database to a known database, Benchmarks:Nodes to a node count it supports, Benchmarks:DataRoot, and Benchmarks:KeyLoadImage for KeyLoad.";

    public string? Database
    {
        get; set;
    }

    public int Nodes
    {
        get; set;
    }

    /// <summary>Release-published KeyLoad server image reference pinned to the digest resolved for this run.</summary>
    public string? KeyLoadImage
    {
        get; set;
    }

    public string? DataRoot
    {
        get; set;
    }

    internal bool IsValid() => Database is not null && BenchmarkDatabases.Supports(Database, Nodes)
        && !string.IsNullOrWhiteSpace(DataRoot)
        && (Database != BenchmarkDatabases.KeyLoad || KeyLoadReleaseImage.IsValid(KeyLoadImage));

    [ConfigurationBinding]
    internal static BenchmarkTopologyOptions Read(IConfiguration configuration)
        => Bind<BenchmarkTopologyOptions>(configuration.GetSection(Section), options => options.IsValid(), Invalid).Value;

    [ConfigurationBinding]
    internal static IOptions<BenchmarkDeploymentOptions> ReadDeployment(IConfiguration configuration)
        => Bind<BenchmarkDeploymentOptions>(configuration.GetSection(BenchmarkDeploymentOptions.SectionName),
            options => options.IsValid(), BenchmarkDeploymentOptions.ValidationMessage);

    [ConfigurationBinding]
    internal static IOptions<T> Bind<T>(IConfiguration section, Func<T, bool> predicate, string message) where T : class, new()
    {
        var options = new OptionsManager<T>(new OptionsFactory<T>([new ConfigureFromConfigurationOptions<T>(section)], [],
            [new ValidateOptions<T>(Options.DefaultName, predicate, message)]));
        _ = options.Value;
        return options;
    }
}
