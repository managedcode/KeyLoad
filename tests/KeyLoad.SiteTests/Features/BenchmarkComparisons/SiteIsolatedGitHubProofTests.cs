namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedGitHubProofTests
{
    [Test]
    public async Task AC_ISO_007_Original277FilesBindFresh270JobsAndCommonNativeImages()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var inputs = await SiteIsolatedGitHubInputs.ReadAsync(token);
        var site = SiteTestInputs.Read();
        var result = await SiteIsolatedGitHubNodeProcess.RunAsync(site.Repository, new
        {
            operation = SiteIsolatedGitHubFields.InputsOperation,
            repository = site.Repository,
            arguments = new { input = inputs.Capture, receipt = inputs.Receipt },
        }, token);
        await Assert.That(result.GetProperty(SiteIsolatedFields.Ok).GetBoolean()).IsTrue();
        await Assert.That(result.GetProperty(SiteIsolatedFields.Result).GetProperty(SiteIsolatedFields.Workers).GetInt32())
            .IsEqualTo(SiteIsolatedGitHubTokens.WorkerCount);
        await Assert.That(result.GetProperty(SiteIsolatedFields.Result).GetProperty(SiteIsolatedFields.Files).GetInt32())
            .IsEqualTo(SiteIsolatedGitHubTokens.FileCount);
    }
}
