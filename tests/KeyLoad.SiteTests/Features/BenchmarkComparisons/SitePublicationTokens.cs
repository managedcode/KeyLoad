namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SitePublicationTokens
{
    public const string SourceRevisionEnvironment = "KEYLOAD_SITE_SOURCE_REVISION";
    public const string ControlRevisionEnvironment = "KEYLOAD_SITE_CONTROL_REVISION";
    public const string GitExecutable = "git";
    public const string GitDirectoryArgument = "-C";
    public const string GitRevisionArgument = "rev-parse";
    public const string GitHeadArgument = "HEAD";
    public const string SourceFailure = "The qualified website revision must match the actual checkout HEAD.";
    public const string MissingArchivePreparation = "Mandatory authenticated GitHub archive preparation did not complete.";
    public const string EvidenceToolsPrefix = "scripts/Features/BenchmarkComparisons/";
    public const string EvidenceModulePrefix = "github-evidence";
    public const string IsolatedEvidenceModulePrefix = "site-isolated-github-";
}
