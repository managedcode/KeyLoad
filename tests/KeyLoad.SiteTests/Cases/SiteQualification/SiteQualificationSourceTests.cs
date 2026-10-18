namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>AC-BC-028/8: provenance uses the actual website checkout.</summary>
internal sealed class SiteQualificationSourceTests
{
    [Test]
    public async Task ActualWebsiteCheckoutRevisionIsAcceptedAsync()
    {
        var inputs = SiteTestInputs.Read();
        await SiteQualificationSource.RequireCheckoutAsync(inputs.Repository, inputs.SiteRevision,
            TestContext.Current!.Execution.CancellationToken);
    }

    [Test]
    public async Task DifferentAndMalformedWebsiteRevisionsAreRejectedAsync()
    {
        var inputs = SiteTestInputs.Read();
        foreach (var revision in new[] { SiteVendorTokens.WrongSourceCommit, SiteTokens.MalformedHash })
        {
            var rejected = false;
            try
            {
                await SiteQualificationSource.RequireCheckoutAsync(inputs.Repository, revision,
                    TestContext.Current!.Execution.CancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                rejected = exception.Message == SitePublicationTokens.SourceFailure;
            }

            await Assert.That(rejected).IsTrue();
        }
    }
}
