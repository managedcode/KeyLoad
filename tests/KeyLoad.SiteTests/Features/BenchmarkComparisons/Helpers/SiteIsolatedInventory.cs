using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>Archive shape follows the exact authenticated producer source, never a supplied cell count.</summary>
internal static class SiteIsolatedInventory
{
    private const int HistoricalWorkers = 270;
    private const int HistoricalFiles = 277;
    private const int CurrentControlWorkers = 330;
    private static readonly string[] HistoricalSources =
    [
        "aa49aa93982866b85a80e771a749d9b968ffc9f2", "73aebfd3f72695357834599e813aba77b9e274ad",
        "77167cbca9efe8942aab869dd52ad5b0b6cc72a1", "ff0af70a279b6adce653bc5fe2527fef51f9de69",
    ];

    internal static bool IsHistorical(JsonObject metadata) => IsHistoricalSource(metadata[SiteIsolatedGitHubTokens.Source]?
        [SiteIsolatedGitHubTokens.Measured]?.GetValue<string>());

    internal static bool IsHistoricalSource(string? source) => HistoricalSources.Contains(source, StringComparer.Ordinal);

    internal static int Workers(JsonObject metadata) => IsHistorical(metadata) ? HistoricalWorkers : SiteIsolatedGitHubTokens.WorkerCount;

    internal static int Files(JsonObject metadata) => IsHistorical(metadata) ? HistoricalFiles : SiteIsolatedGitHubTokens.FileCount;

    internal static int ControlWorkers(string source) => IsHistoricalSource(source) ? HistoricalWorkers : CurrentControlWorkers;
}
