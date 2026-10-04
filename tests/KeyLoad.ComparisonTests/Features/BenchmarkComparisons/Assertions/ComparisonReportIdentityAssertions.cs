using System.Globalization;
using System.Text.Json;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class ComparisonReportIdentityAssertions
{
    private const string WorkflowEnvironment = "GITHUB_WORKFLOW";
    private const string ProfileEnvironment = "Benchmarks__EvidenceProfile";
    private const string ProvenanceProperty = "provenance";
    private const string RunnerImageProperty = "loadGeneratorImage";
    private const string TimeSeriesProfile = "timeseries";

    /// <summary>AC-IMAGE-005 binds actual normal report metadata to the job-owned image receipt.</summary>
    internal static async Task VerifyAsync(ComparisonReport report, CancellationToken cancellationToken)
    {
        var receipt = await ComparisonImageReceipt.ReadAsync(cancellationToken);
        await VerifyProvenanceAsync(report.Provenance,
            ComparisonImageProtocol.RequiredEnvironment(ProfileEnvironment));
        await Assert.That(report.LoadGeneratorImage).IsEqualTo(receipt.RunnerImage);
        await Assert.That(report.SourceRevision)
            .IsEqualTo(ComparisonImageProtocol.RequiredEnvironment(ComparisonImageProtocol.ShaEnvironment));
        await Assert.That(report.Targets.Single(target => target.Name == "KeyLoad").Image).IsEqualTo(receipt.ServerImage);
    }

    /// <summary>AC-IMAGE-005 preserves the separate TimeSeries schema and checks true container provenance.</summary>
    internal static async Task<ComparisonImageReceipt> VerifyTimeSeriesAsync(JsonElement report,
        CancellationToken cancellationToken)
    {
        var receipt = await ComparisonImageReceipt.ReadAsync(cancellationToken);
        var provenance = report.GetProperty(ProvenanceProperty).Deserialize<GitHubProvenance>(ReportWriter.JsonOptions);
        await VerifyProvenanceAsync(provenance, TimeSeriesProfile);
        await Assert.That(report.GetProperty(RunnerImageProperty).GetString()).IsEqualTo(receipt.RunnerImage);
        return receipt;
    }

    private static async Task VerifyProvenanceAsync(GitHubProvenance? provenance, string profile)
    {
        await Assert.That(provenance).IsNotNull();
        await Assert.That(provenance!.RunId).IsEqualTo(long.Parse(
            ComparisonImageProtocol.RequiredEnvironment(ComparisonImageProtocol.RunEnvironment), CultureInfo.InvariantCulture));
        await Assert.That(provenance.Attempt).IsEqualTo(int.Parse(
            ComparisonImageProtocol.RequiredEnvironment(ComparisonImageProtocol.AttemptEnvironment), CultureInfo.InvariantCulture));
        await Assert.That(provenance.Repository)
            .IsEqualTo(ComparisonImageProtocol.RequiredEnvironment(ComparisonImageProtocol.RepositoryEnvironment));
        await Assert.That(provenance.Ref)
            .IsEqualTo(ComparisonImageProtocol.RequiredEnvironment(ComparisonImageProtocol.RefEnvironment));
        await Assert.That(provenance.Workflow).IsEqualTo(ComparisonImageProtocol.RequiredEnvironment(WorkflowEnvironment));
        await Assert.That(provenance.Profile).IsEqualTo(profile);
    }
}
