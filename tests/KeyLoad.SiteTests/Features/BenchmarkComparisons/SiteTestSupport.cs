using System.Text.Json;
using System.Text.RegularExpressions;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteTokens
{
    public const string RepositoryEnvironment = "KEYLOAD_SITE_REPOSITORY";
    public const string NodeRepositoryEnvironment = RepositoryEnvironment;
    public const string EvidenceBase = "https://github.com/managedcode/KeyLoad/actions/runs/";
    public const string RunPattern = "^[1-9][0-9]*$";
    public const string RevisionPattern = "^[0-9a-f]{40}$";
    public const string NodeExecutable = "node";
    public const string MalformedHash = "not-a-sha256-value";
    public const string SiteTestsMissingProvenance = "Site tests require the original complete current GitHub evidence and exact source paths.";
    public const string SiteTestsMissingEnvironment = "Required site-test provenance is not configured.";
    public const string NodeProbeDidNotStart = "The real Node probe process did not start.";
    public const string NodeProbeFailure = "Node probe failed with exit";
    public const string NodeOutputExceeded = "The Node probe exceeded the bounded process output.";
    public const string BuilderOutputExceeded = "The site builder exceeded the bounded process output.";
    public const string StaticHostAddress = "http://127.0.0.1:";
    public const string UrlPathSeparator = "/";
    public const char UrlPathSeparatorCharacter = '/';
    public const string StaticHostBindFailure = "The bounded static evidence listener could not bind an ephemeral port.";
    public const int StaticHostTimeoutMilliseconds = 5000;
    public const int StaticHostStartAttempts = 3;
    public const int PortZero = 0;
    public const int MaximumStaticPathCharacters = 1024;
    public const string StaticGetMethod = "GET";
    public const int ProcessOutputBufferCharacters = 4096;
    public const int ProcessSuccessExitCode = 0;
    public const int Zero = 0;
    public const string DataDirectory = "data";
    public const string MeasuredSourceRevision = "measuredSourceRevision";
    public const string EvidenceUrl = "evidenceUrl";
    public const string Sha256 = "sha256";
    public const string Value = "value";
    public const string Result = "result";
    public const string JsonSuffix = ".json";
    public const int One = 1;
    public const int ErrorPercentageScale = 100;
    public const int NodeTimeoutMilliseconds = 15000;
    public const int CleanupTimeoutMilliseconds = 3000;
    public const int MaximumNodeOutputCharacters = 2_000_000;

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };
    public static readonly Regex EvidenceRunPattern = new(RunPattern, RegexOptions.CultureInvariant);
    public static readonly Regex MeasuredRevisionPattern = new(RevisionPattern, RegexOptions.CultureInvariant);
}

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
            website != Environment.GetEnvironmentVariable(SitePublicationTokens.SourceRevisionEnvironment) ||
            control != Environment.GetEnvironmentVariable(SitePublicationTokens.ControlRevisionEnvironment) ||
            run.GetProperty(ReceiptRunUrl).GetString() != SiteTokens.EvidenceBase + runId ||
            !Directory.Exists(repository) || !File.Exists(Path.Combine(aggregate, SiteIsolatedGitHubTokens.Manifest)) ||
            !File.Exists(Path.Combine(repository, SiteAssetTokens.BuilderRelativePath)))
        {
            throw new InvalidDataException(SiteTokens.SiteTestsMissingProvenance);
        }
        return new(repository, aggregate, runId, measured, website);
    }
}
