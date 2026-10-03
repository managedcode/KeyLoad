namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

[NotInParallel(SiteIsolatedGitHubFields.NativeFixtureKey)]
internal sealed class SiteIsolatedGitHubIntegrityTests
{
    [Test]
    public async Task AC_ISO_007_RewrittenPrivateFilesCannotReplaceOriginalZipOrInMemoryAuthority()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteIsolatedGitHubIntegrityScope.CreateAsync(token);
        await SiteIsolatedGitHubArchiveSetup.VerifyUnchangedAsync(scope.Original, token);
        try
        {
            await SiteIsolatedGitHubInputCorruption.VerifyAsync(scope.PrivateReceipt, token);
        }
        finally
        {
            await SiteIsolatedGitHubArchiveSetup.VerifyUnchangedAsync(scope.Original, token);
        }
    }
}
