using System.Text.Json;
using System.Text.RegularExpressions;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteTokens
{
    public const string ReportsEnvironment = "KEYLOAD_SITE_REPORTS";
    public const string RepositoryEnvironment = "KEYLOAD_SITE_REPOSITORY";
    public const string EvidenceRunEnvironment = "KEYLOAD_SITE_EVIDENCE_RUN";
    public const string MeasuredRevisionEnvironment = "KEYLOAD_SITE_MEASURED_REVISION";
    public const string GitHubShaEnvironment = "GITHUB_SHA";
    public const string NodeRepositoryEnvironment = RepositoryEnvironment;
    public const string EvidenceBase = "https://github.com/managedcode/KeyLoad/actions/runs/";
    public const string RepositoryWebBase = "https://github.com/managedcode/KeyLoad";
    public const string RunPattern = "^[1-9][0-9]*$";
    public const string RevisionPattern = "^[0-9a-f]{40}$";
    public const string NodeExecutable = "node";
    public const string StandardErrorLabel = "stderr:";
    public const string ZeroRepetition = "0";
    public const string InvalidJson = "{invalid";
    public const string InvalidReportPath = "../outside/results.json";
    public const string ForeignEvidenceUrl = "https://example.invalid/run";
    public const string CommittedSourceKind = "committed_source";
    public const string DocumentationBlobPath = "/blob/";
    public const string DocumentationTreePath = "/tree/";
    public const string ArchitectureDocPath = "docs/Architecture.md";
    public const string DocsDirectory = "docs";
    public const string StaticHostCredentialUser = "site-test-user";
    public const string MalformedHash = "not-a-sha256-value";
    public const string LoadReportOperation = "loadReport";
    public const string OracleMetricError = "Oracle metric is outside the frozen set.";
    public const string SiteTestsMissingProvenance = "Site tests require the frozen successful GitHub evidence and repository paths.";
    public const string SiteTestsMissingProfile = "An authentic historical comparison profile is missing.";
    public const string SiteTestsMissingEnvironment = "Required site-test provenance is not configured.";
    public const string SiteTestsNonAbsolutePath = "Required site-test path is not absolute.";
    public const string NodeProbeDidNotStart = "The real Node probe process did not start.";
    public const string NodeProbeFailure = "Node probe failed with exit";
    public const string NodeOutputExceeded = "The Node probe exceeded the bounded process output.";
    public const string BuilderDidNotStart = "The real site builder process did not start.";
    public const string BuilderOutputExceeded = "The site builder exceeded the bounded process output.";
    public const string DataDirectoryUrl = "data/";
    public const string MissingDataDirectoryUrl = "missing/";
    public const string StaticHostAddress = "http://127.0.0.1:";
    public const string UrlPathSeparator = "/";
    public const char UrlPathSeparatorCharacter = '/';
    public const string StaticHostBindFailure = "The bounded static evidence listener could not bind an ephemeral port.";
    public const int StaticHostTimeoutMilliseconds = 5000;
    public const int StaticHostStartAttempts = 3;
    public const int PortZero = 0;
    public const int MaximumStaticRequests = 8;
    public const int MaximumStaticPathCharacters = 1024;
    public const long MaximumStaticFileBytes = 2_000_000;
    public const string StaticGetMethod = "GET";
    public const int ProcessOutputBufferCharacters = 4096;
    public const int ProcessSuccessExitCode = 0;
    public const char HexZero = '0';
    public const int Zero = 0;
    public const string DataDirectory = "data";
    public const string RunsDirectory = "runs";
    public const string CatalogFile = "catalog.json";
    public const string ReportFile = "results.json";
    public const string CsvFile = "samples.csv";
    public const string MarkdownFile = "results.md";
    public const string SmokeProfile = "smoke";
    public const string SmallProfile = "json-1k-c8";
    public const string LargeProfile = "json-16k-c4";
    public const string SchemaVersion = "schemaVersion";
    public const string GeneratedAt = "generatedAt";
    public const string SiteSourceRevision = "siteSourceRevision";
    public const string SiteSourceKind = "siteSourceKind";
    public const string MeasuredSourceRevision = "measuredSourceRevision";
    public const string EvidenceUrl = "evidenceUrl";
    public const string Runs = "runs";
    public const string Id = "id";
    public const string Name = "name";
    public const string Label = "label";
    public const string Report = "report";
    public const string StartedAt = "startedAt";
    public const string SourceRevision = "sourceRevision";
    public const string Sha256 = "sha256";
    public const string DatasetSha256 = "datasetSha256";
    public const string Options = "options";
    public const string Operations = "operations";
    public const string Cases = "cases";
    public const string Targets = "targets";
    public const string Target = "target";
    public const string Scenario = "scenario";
    public const string Repetition = "repetition";
    public const string Status = "status";
    public const string Measurement = "measurement";
    public const string Value = "value";
    public const string Minimum = "min";
    public const string Maximum = "max";
    public const string Throughput = "throughput";
    public const string Detail = "detail";
    public const string Attempts = "attempts";
    public const string Successes = "successes";
    public const string Failures = "failures";
    public const string Latency = "latency";
    public const string Enqueue = "enqueue";
    public const string Receive = "receive";
    public const string Ack = "ack";
    public const string ClientResources = "clientResources";
    public const string P50 = "p50Ms";
    public const string RowP50 = "p50";
    public const string RowP95 = "p95";
    public const string RowP99 = "p99";
    public const string Samples = "samples";
    public const string Result = "result";
    public const string Ok = "ok";
    public const string Error = "error";
    public const string ReportsArgument = "--reports";
    public const string OutputArgument = "--output";
    public const string RevisionArgument = "--revision";
    public const string EvidenceArgument = "--evidence-url";
    public const string SiteRevisionArgument = "--site-revision";
    public const string RowsOperation = "rows";
    public const string MedianOperation = "median";
    public const string CatalogOperation = "validateCatalog";
    public const string ReportOperation = "validateReport";
    public const string HashOperation = "hash";
    public const string InvalidOperation = "unknown-operation";
    public const string ThroughputMetric = "throughput";
    public const string P50Metric = "p50";
    public const string P95Metric = "p95";
    public const string P99Metric = "p99";
    public const string ErrorMetric = "errors";
    public const string EnqueueMetric = "enqueue";
    public const string ReceiveMetric = "receive";
    public const string AckMetric = "ack";
    public const string CpuMetric = "cpu";
    public const string AllocationMetric = "alloc";
    public const string RssMetric = "rss";
    public const string PointRead = "PointRead";
    public const string DocumentWrite = "DocumentWrite";
    public const string VectorExact = "VectorExact";
    public const string QueueCycle = "QueueCycle";
    public const string GraphNeighbors = "GraphNeighbors";
    public const string GraphTraverse = "GraphTraverse";
    public const string MedianRepetition = "median";
    public const string MeasuredStatus = "measured";
    public const string FailedStatus = "failed";
    public const string KeyLoadTarget = "KeyLoad";
    public const string ControlledInvalidPrefix = ".controlled-invalid-";
    public const string UnsupportedStatus = "unsupported";
    public const string PreviewKind = "local_preview";
    public const string JsonSuffix = ".json";
    public const int ReportSchemaNumber = 2;
    public const int CatalogSchemaNumber = 1;
    public const int ShaLength = 40;
    public const int HistoricalTargetCount = 6;
    public const int One = 1;
    public const int MedianDivisor = 2;
    public const int ErrorPercentageScale = 100;
    public const int MillisecondsPerSecond = 1000;
    public const int BytesPerKilobyte = 1024;
    public const int BytesPerMebibyte = 1_048_576;
    public const byte JsonLeadingWhitespace = 32;
    public const int NodeTimeoutMilliseconds = 15000;
    public const int CleanupTimeoutMilliseconds = 3000;
    public const int MaximumNodeOutputCharacters = 2_000_000;
    public const double NumericTolerance = 0.0000001;

    public static readonly string[] ProfileNames = [SmokeProfile, SmallProfile, LargeProfile];
    public static readonly string[] Scenarios = [PointRead, DocumentWrite, VectorExact, QueueCycle, GraphNeighbors, GraphTraverse];
    public static readonly string[] Metrics = [ThroughputMetric, P50Metric, P95Metric, P99Metric, ErrorMetric,
        EnqueueMetric, ReceiveMetric, AckMetric, CpuMetric, AllocationMetric, RssMetric];
    public static readonly double[] OddMedianValues = [9, 2, 5];
    public static readonly double[] EvenMedianValues = [8, 2, 6, 4];
    public static readonly double[] CancellationMedianValues = [1];
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };
    public static readonly Regex EvidenceRunPattern = new(RunPattern, RegexOptions.CultureInvariant);
    public static readonly Regex MeasuredRevisionPattern = new(RevisionPattern, RegexOptions.CultureInvariant);
}

internal sealed record SiteTestInputs(string Repository, string Reports, string EvidenceRun,
    string MeasuredRevision, string SiteRevision)
{
    public string EvidenceUrl => SiteTokens.EvidenceBase + EvidenceRun;

    public static SiteTestInputs Read()
    {
        var repository = RequiredEnvironment(SiteTokens.RepositoryEnvironment, path: true);
        var reportsValue = RequiredEnvironment(SiteTokens.ReportsEnvironment, path: true);
        var run = RequiredEnvironment(SiteTokens.EvidenceRunEnvironment);
        var revision = RequiredEnvironment(SiteTokens.MeasuredRevisionEnvironment);
        var siteRevision = RequiredEnvironment(SitePublicationTokens.SourceRevisionEnvironment);
        var reports = Path.GetFullPath(reportsValue);
        if (!SiteTokens.EvidenceRunPattern.IsMatch(run) || !SiteTokens.MeasuredRevisionPattern.IsMatch(revision) ||
            !SiteTokens.MeasuredRevisionPattern.IsMatch(siteRevision) ||
            !Directory.Exists(repository) || !Directory.Exists(reports) || !File.Exists(Path.Combine(repository, SiteAssetTokens.BuilderRelativePath)))
        {
            throw new InvalidOperationException(SiteTokens.SiteTestsMissingProvenance);
        }

        foreach (var profile in SiteTokens.ProfileNames)
        {
            if (!File.Exists(Path.Combine(reports, profile, SiteTokens.ReportFile)) ||
                !File.Exists(Path.Combine(reports, profile, SiteTokens.CsvFile)) ||
                !File.Exists(Path.Combine(reports, profile, SiteTokens.MarkdownFile)))
            {
                throw new InvalidOperationException(SiteTokens.SiteTestsMissingProfile);
            }

            using var report = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(reports, profile, SiteTokens.ReportFile)));
            if (report.RootElement.GetProperty(SiteTokens.SchemaVersion).GetInt32() != SiteTokens.ReportSchemaNumber ||
                report.RootElement.GetProperty(SiteTokens.SourceRevision).GetString() != revision)
            {
                throw new InvalidOperationException(SiteTokens.SiteTestsMissingProfile);
            }
        }

        return new(repository, reports, run, revision, siteRevision);
    }

    private static string RequiredEnvironment(string name, bool path = false)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(SiteTokens.SiteTestsMissingEnvironment);
        }

        if (path && !Path.IsPathFullyQualified(value))
        {
            throw new InvalidOperationException(SiteTokens.SiteTestsNonAbsolutePath);
        }

        return value;
    }
}

internal sealed record SiteReport
{
    public string SourceRevision { get; set; } = string.Empty;
    public string DatasetSha256 { get; set; } = string.Empty;
    public SiteReportOptions Options { get; set; } = new();
    public List<SiteTarget> Targets { get; set; } = [];
    public List<SiteCase> Cases { get; set; } = [];
}

internal sealed record SiteReportOptions
{
    public int Operations { get; set; }
    public int Repetitions { get; set; }
}

internal sealed record SiteTarget
{
    public string Name { get; set; } = string.Empty;
}

internal sealed record SiteCase
{
    public string Target { get; set; } = string.Empty;
    public string Scenario { get; set; } = string.Empty;
    public int Repetition { get; set; }
    public string Status { get; set; } = string.Empty;
    public SiteMeasurement? Measurement { get; set; }
    public string? Detail { get; set; }
}

internal sealed record SiteMeasurement
{
    public int Attempts { get; set; }
    public int Successes { get; set; }
    public int Failures { get; set; }
    public double UsefulOperationsPerSecond { get; set; }
    public SiteLatency Latency { get; set; } = new();
    public SiteLatency? Enqueue { get; set; }
    public SiteLatency? Receive { get; set; }
    public SiteLatency? Ack { get; set; }
    public SiteClientResources? ClientResources { get; set; }
}

internal sealed record SiteLatency
{
    public double P50Ms { get; set; }
    public double P95Ms { get; set; }
    public double P99Ms { get; set; }
}

internal sealed record SiteClientResources
{
    public double CpuSeconds { get; set; }
    public long AllocatedBytes { get; set; }
    public long PeakObservedWorkingSetBytes { get; set; }
}
