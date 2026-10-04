using System.Collections.Immutable;
using System.Text.Json;

namespace KeyLoad.Comparisons;

/// <summary>Write-only report projection which streams its existing case array.</summary>
internal sealed record StreamedComparisonReport(int SchemaVersion, Guid RunId, DateTimeOffset StartedAt,
    ComparisonOptions Options, string DatasetSha256, string LoadModel, string HostOs, string Architecture,
    int LogicalProcessors, string Runtime, string Storage, string? SourceRevision,
    ImmutableArray<TargetProfile> Targets, IAsyncEnumerable<StreamedComparisonCase> Cases)
{
    /// <summary>Gets GitHub Actions provenance for the report, when available.</summary>
    public GitHubProvenance? Provenance { get; init; }

    /// <summary>Gets the load-generator image reference, when available.</summary>
    public string? LoadGeneratorImage { get; init; }

    internal static StreamedComparisonReport Create(ComparisonReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Cases.IsDefault)
        {
            throw new JsonException(ImmutableArrayAsyncView.InvalidCollectionMessage);
        }

        var cases = ImmutableArrayAsyncView.Create(report.Cases, StreamedComparisonCase.Create);
        return new StreamedComparisonReport(report.SchemaVersion, report.RunId, report.StartedAt, report.Options,
            report.DatasetSha256, report.LoadModel, report.HostOs, report.Architecture, report.LogicalProcessors,
            report.Runtime, report.Storage, report.SourceRevision, report.Targets, cases)
        {
            Provenance = report.Provenance,
            LoadGeneratorImage = report.LoadGeneratorImage
        };
    }
}
