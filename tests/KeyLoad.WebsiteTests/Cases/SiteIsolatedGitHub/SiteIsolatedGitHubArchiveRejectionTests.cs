namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal sealed class SiteIsolatedGitHubArchiveRejectionTests
{
    [Test]
    [Arguments(SiteIsolatedGitHubArchiveMutation.Traversal, SiteIsolatedGitHubTokens.InvalidPath)]
    [Arguments(SiteIsolatedGitHubArchiveMutation.Duplicate, SiteIsolatedGitHubTokens.Duplicate)]
    [Arguments(SiteIsolatedGitHubArchiveMutation.CaseDuplicate, SiteIsolatedGitHubTokens.Duplicate)]
    [Arguments(SiteIsolatedGitHubArchiveMutation.Symlink, SiteIsolatedGitHubTokens.InvalidPath)]
    [Arguments(SiteIsolatedGitHubArchiveMutation.Special, SiteIsolatedGitHubTokens.InvalidPath)]
    [Arguments(SiteIsolatedGitHubArchiveMutation.Missing, SiteIsolatedGitHubTokens.Incomplete)]
    public async Task AC_ISO_007_CompleteBclPreflightRejectsCorruptArchiveBeforeOwnedOutput(
        SiteIsolatedGitHubArchiveMutation mutation, string expected)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var inputs = await SiteIsolatedGitHubInputs.ReadAsync(token);
        await using var temporary = SiteTempDirectory.Create();
        var corrupt = Path.Combine(temporary.Path, SiteIsolatedGitHubFields.ControlledArchive);
        await SiteIsolatedGitHubArchiveMutationWriter.CreateAsync(inputs, corrupt, mutation, token);
        var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            SiteSessionOriginalArchives.VerifyWorkerAsync(corrupt,
                SiteIsolatedGitHubArchivePaths.ExpectedWorker(inputs.Metadata), token));
        await Assert.That(error!.Message).IsEqualTo(expected);
        await Assert.That(Directory.Exists(temporary.Output)).IsFalse();
    }
}
