namespace KeyLoad.SiteTests.BenchmarkComparisons;

internal sealed record SiteContentInputs(string Repository, string SiteRevision)
{
    public static SiteContentInputs FromEnvironment()
    {
        var repository = SiteEnvironmentConfiguration.Capture().Value.Repository;
        var revision = SiteEnvironmentConfiguration.Capture().Value.SourceRevision;
        if (repository is null || !Path.IsPathFullyQualified(repository) || !Directory.Exists(repository) ||
            !SiteCoverageSourceManifestWriter.IsRevision(revision))
        {
            throw new InvalidOperationException(SiteCoverageTokens.InvalidRootFailure);
        }

        return new(Path.GetFullPath(repository), revision!);
    }
}
