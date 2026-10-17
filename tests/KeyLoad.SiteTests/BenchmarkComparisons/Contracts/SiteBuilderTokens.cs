namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteBuilderTokens
{
    public const string EvidenceDirectoryName = "builder-invocations";
    public const string InvocationFile = "invocation.json";
    public const string StandardOutputFile = "stdout.txt";
    public const string StandardErrorFile = "stderr.txt";
    public const string EntryBuilderPath = "site/scripts/build.mjs";
    public const string FeatureBuilderPath = "site/Features/BenchmarkComparisons/build-site.mjs";
    public const string IsolatedArgument = "--isolated=";
    public const string OutputArgument = "--output=";
    public const string RevisionArgument = "--revision=";
    public const string EvidenceArgument = "--evidence-url=";
    public const string SiteRevisionArgument = "--site-revision=";
    public const string SchemaVersion = "schemaVersion";
    public const string SiteSourceRevision = "siteSourceRevision";
    public const string Cohort = "cohort";
    public const string Arguments = "arguments";
    public const string Sources = "sources";
    public const string Path = "path";
    public const string Sha256 = "sha256";
    public const string ExitCode = "exitCode";
    public const string Stdout = "stdout";
    public const string Stderr = "stderr";
    public const string Output = "output";
    public const string JavascriptGzipBytes = "javascriptGzipBytes";
    public const string CssGzipBytes = "cssGzipBytes";
    public const string ArgumentError = "Use unique known --name=value arguments.";
    public const string UnknownArgument = "--unsupported-site-test-argument";
    public const string SymlinkError = "Symlinks and non-regular evidence/assets are not accepted.";
    public const string VendorError = "Official Three vendor manifest or bytes differ.";
    public const string BuilderFailurePrefix = "The site builder failed before Chrome launch with exit code";
    public const string ErrorSeparator = "; stderr: ";
    public const string ReceiptMissing = "The real site builder invocation receipt was not retained.";
    public const string CoverageRootMissing = "The configured absolute site coverage root is required for builder evidence.";
    public const int ReceiptSchemaVersion = 1;
    public const int ExpectedBuilderSourceCount = 2;
    public const int AuthoredJavaScriptGzipLimit = 40_960;
    public const int AuthoredCssGzipLimit = 20_480;
}
