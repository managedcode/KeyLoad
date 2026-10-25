namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal sealed class SiteMetadataRejectionTests
{
    [Test]
    [Arguments("missing-icon")]
    [Arguments("linked-icon")]
    [Arguments("missing-card")]
    [Arguments("linked-card")]
    public async Task AC_SEO_005_RealBuilderRejectsMissingOrLinkedMetadataAssetsBeforeOutput(string condition)
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteMetadataTestScope.CreateAsync(fixture, token);
        var source = SourcePath(scope.Repository, condition);
        if (condition.StartsWith("missing", StringComparison.Ordinal))
        {
            File.Delete(source);
        }
        else
        {
            var target = source + ".target";
            File.Move(source, target);
            File.CreateSymbolicLink(source, target);
        }

        var rejected = await SiteIsolatedBuilderProcess.RunAsync(scope.Fixture, scope.Output, token);
        await Assert.That(rejected.ExitCode).IsNotEqualTo(SiteTokens.ProcessSuccessExitCode);
        await Assert.That(rejected.StandardError.Length).IsGreaterThan(0);
        if (condition.StartsWith("linked", StringComparison.Ordinal))
        {
            await Assert.That(rejected.StandardError).Contains(SiteBuilderTokens.SymlinkError);
        }
        else
        {
            await Assert.That(rejected.StandardError).Contains(Path.GetFileName(source));
        }
        await Assert.That(Directory.Exists(scope.Output)).IsFalse();
    }

    private static string SourcePath(string root, string condition)
    {
        var relative = condition.EndsWith("icon", StringComparison.Ordinal)
            ? Path.Combine(SiteMetadataTokens.SiteDirectory, SiteMetadataTokens.Icon192)
            : Path.Combine(SiteMetadataTokens.FeaturePath, SiteMetadataTokens.SourceCardPng);
        return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
