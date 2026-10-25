using System.Text.Json;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal sealed record SiteTestInputs(string Repository, string Aggregate, string EvidenceRun,
    string MeasuredRevision, string SiteRevision)
{
    private const string ReceiptSource = "source";
    private const string ReceiptWebsite = "website";
    private const string ReceiptMeasured = "measured";
    private const string ReceiptControl = "control";
    private const string ReceiptRun = "run";
    private const string ReceiptRunId = "id";
    private const string ReceiptRunUrl = "url";

    public string EvidenceUrl => SiteTokens.EvidenceBase + EvidenceRun;

    public static SiteTestInputs Read()
    {
        var repository = SiteIsolatedGitHubInputs.Required(SiteTokens.RepositoryEnvironment);
        var aggregate = SiteIsolatedGitHubInputs.Required(SiteIsolatedGitHubTokens.AggregateEnvironment);
        var receiptPath = SiteIsolatedGitHubInputs.Required(SiteIsolatedGitHubTokens.ReceiptEnvironment);
        var capture = SiteIsolatedGitHubInputs.Required(SiteIsolatedGitHubTokens.CaptureEnvironment);
        if (aggregate != Path.Combine(capture, SiteIsolatedGitHubTokens.Input, SiteIsolatedGitHubTokens.Aggregate) ||
            receiptPath != Path.Combine(capture, SiteIsolatedGitHubTokens.Receipt))
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidReceipt);
        }
        SiteIsolatedGitHubFileOperations.RequireRegular(receiptPath);
        if (new FileInfo(receiptPath).Length > SiteIsolatedGitHubTokens.JsonBytes)
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidReceipt);
        }
        using var document = JsonDocument.Parse(File.ReadAllBytes(receiptPath));
        var sources = document.RootElement.GetProperty(ReceiptSource);
        var website = sources.GetProperty(ReceiptWebsite).GetString() ?? string.Empty;
        var measured = sources.GetProperty(ReceiptMeasured).GetString() ?? string.Empty;
        var control = sources.GetProperty(ReceiptControl).GetString();
        var run = document.RootElement.GetProperty(ReceiptRun);
        var id = run.GetProperty(ReceiptRunId).GetInt64();
        var runId = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (id <= 0 || !SiteTokens.MeasuredRevisionPattern.IsMatch(website) ||
            !SiteTokens.MeasuredRevisionPattern.IsMatch(measured) ||
            website != SiteEnvironmentConfiguration.Capture().Value.SourceRevision ||
            control != SiteEnvironmentConfiguration.Capture().Value.ControlRevision ||
            run.GetProperty(ReceiptRunUrl).GetString() != SiteTokens.EvidenceBase + runId ||
            !Directory.Exists(repository) || !File.Exists(Path.Combine(aggregate, SiteIsolatedGitHubTokens.Manifest)) ||
            !File.Exists(Path.Combine(repository, SiteAssetTokens.BuilderRelativePath)))
        {
            throw new InvalidDataException(SiteTokens.SiteTestsMissingProvenance);
        }
        return new(repository, aggregate, runId, measured, website);
    }
}
