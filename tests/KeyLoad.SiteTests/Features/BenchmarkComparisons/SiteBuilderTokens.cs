namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteBuilderTokens
{
    public const string EvidenceDirectoryName = "builder-invocations";
    public const string InvocationFile = "invocation.json";
    public const string InvocationStagingFile = "invocation.pending";
    public const string StandardOutputFile = "stdout.txt";
    public const string StandardErrorFile = "stderr.txt";
    public const string EntryBuilderPath = "site/scripts/build.mjs";
    public const string FeatureBuilderPath = "site/Features/BenchmarkComparisons/build-site.mjs";
    public const string SchemaVersion = "schemaVersion";
    public const string SiteSourceRevision = "siteSourceRevision";
    public const string MeasuredSourceRevision = "measuredSourceRevision";
    public const string EvidenceRun = "evidenceRun";
    public const string EvidenceUrl = "evidenceUrl";
    public const string WorkingDirectory = "workingDirectory";
    public const string Arguments = "arguments";
    public const string BuilderSources = "builderSources";
    public const string Path = "path";
    public const string ExitCode = "exitCode";
    public const string StandardOutput = "stdoutFile";
    public const string StandardError = "stderrFile";
    public const string Sha256 = "sha256";
    public const string JsonParserMarker = "in JSON";
    public const string ArgumentEquals = "=";
    public const string ArgumentError = "Use unique known --name=value arguments.";
    public const string UnknownArgument = "--unsupported-site-test-argument";
    public const string OutputPathError = "Output must be a nonexistent isolated directory outside source/reports and their ancestors.";
    public const string SymlinkError = "Symlinks and non-regular evidence/assets are not accepted.";
    public const string VendorError = "Official Three vendor manifest or bytes differ.";
    public const string BuilderFailurePrefix = "The site builder failed before Chrome launch with exit code";
    public const string ErrorSeparator = "; stderr: ";
    public const string ReceiptMissing = "The real site builder invocation receipt was not retained.";
    public const string CoverageRootMissing = "The configured absolute site coverage root is required for builder evidence.";
    public const string GuidFormat = "N";
    public const int ReceiptSchemaVersion = 1;
    public const int ExpectedBuilderSourceCount = 2;
}
