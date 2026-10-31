using System.Globalization;
using KeyLoad.ComparisonHost.Measurement;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonHost.Deployment;

/// <summary>The one database, data type and node count this process benchmarks, read from the environment.</summary>
internal sealed record BenchmarkJob(string Database, string Type, int Nodes, string? KeyLoadImage, string Output,
    BenchmarkSettings Settings, string? Commit, long? RunId)
{
    private const string DatabaseKey = "Benchmarks:Database";
    private const string TypeKey = "Benchmarks:Type";
    private const string NodesKey = "Benchmarks:Nodes";
    private const string ImageKey = "Benchmarks:KeyLoadImage";
    private const string OutputKey = "Benchmarks:Output";
    private const string RecordsKey = "Benchmarks:Records";
    private const string OperationsKey = "Benchmarks:Operations";
    private const string CaseMinutesKey = "Benchmarks:CaseMinutes";
    private const string CommitKey = "GITHUB_SHA";
    private const string RunKey = "GITHUB_RUN_ID";
    private const string ResultsDirectory = "results";
    private const string Missing = "Set Benchmarks__Database, Benchmarks__Type and Benchmarks__Nodes for the benchmark job.";

    internal string ResultPath => Path.Combine(Output, FileName(Database, Type, Nodes));

    internal static BenchmarkJob Read()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var database = configuration[DatabaseKey];
        var type = configuration[TypeKey];
        if (string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(type)
            || !int.TryParse(configuration[NodesKey], NumberStyles.None, CultureInfo.InvariantCulture, out var nodes))
        {
            throw new InvalidOperationException(Missing);
        }
        var settings = new BenchmarkSettings(
            Number(configuration, RecordsKey, BenchmarkSettings.StandardRecords),
            Number(configuration, OperationsKey, BenchmarkSettings.StandardOperations),
            BenchmarkSettings.StandardClients, BenchmarkSettings.StandardSeed,
            TimeSpan.FromMinutes(Number(configuration, CaseMinutesKey, BenchmarkSettings.StandardCaseMinutes)));
        var output = configuration[OutputKey] is { Length: > 0 } configured
            ? Path.GetFullPath(configured) : Path.Combine(AppContext.BaseDirectory, ResultsDirectory);
        var runId = long.TryParse(configuration[RunKey], NumberStyles.None, CultureInfo.InvariantCulture, out var run) ? run : (long?)null;
        return new(database, type, nodes, configuration[ImageKey], output, settings, configuration[CommitKey], runId);
    }

    /// <summary>The result file name, for example <c>postgresql-pgvector-vector-n3.json</c>.</summary>
    internal static string FileName(string database, string type, int nodes)
    {
        var slug = new string([.. database.Select(character => char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')]);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }
        return slug.Trim('-') + "-" + type + "-n" + nodes.ToString(CultureInfo.InvariantCulture) + ".json";
    }

    private static int Number(IConfiguration configuration, string key, int standard)
        => int.TryParse(configuration[key], NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : standard;
}
