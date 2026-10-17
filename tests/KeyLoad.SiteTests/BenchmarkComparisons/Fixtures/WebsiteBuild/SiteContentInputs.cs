namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteContentInputs(string Repository, string SiteRevision)
{
    public static SiteContentInputs FromEnvironment()
    {
        var repository = Environment.GetEnvironmentVariable(SiteTokens.RepositoryEnvironment);
        var revision = Environment.GetEnvironmentVariable(SitePublicationTokens.SourceRevisionEnvironment);
        if (repository is null || !Path.IsPathFullyQualified(repository) || !Directory.Exists(repository) ||
            !SiteCoverageSourceManifestWriter.IsRevision(revision))
        {
            throw new InvalidOperationException(SiteCoverageTokens.InvalidRootFailure);
        }

        return new(Path.GetFullPath(repository), revision!);
    }
}
