namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedCoverageInventoryTests
{
    private const string IsolatedPath = SitePublicationTokens.EvidenceToolsPrefix + "site-isolated-github-controlled.mjs";
    [Test]
    [Arguments(IsolatedPath, true)]
    [Arguments(IsolatedPath, false)]
    public async Task AC_ISO_009_NativeCoverageRequiresCurrentEvidenceToolInventory(string relative, bool inventoried)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = new SiteCoverageFixtureDirectory();
        await temporary.CreateAsync(token);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(temporary.Root, relative))!);
        var bytes = await temporary.WriteSourceAsync(relative, SiteCoverageNativeTestSupport.FixtureSource, token);
        var receipt = await SiteCoverageNativeTestSupport.RunNodeCoverageForAsync(temporary,
            SiteCoverageNativeTestSupport.TrueArgument, relative, token);
        var sources = new Dictionary<string, SiteCoverageSourceEntry>(StringComparer.Ordinal);
        if (inventoried)
        {
            sources.Add(relative, new(relative, SiteCoverageSourceManifestWriter.Hash(bytes),
                SiteCoverageNativeTestSupport.FixtureSource.Length));
        }

        if (inventoried)
        {
            var snapshots = SiteCoverageNativeTestSupport.ParseSnapshots(receipt, temporary.Root, sources);
            await Assert.That(snapshots.Count).IsEqualTo(SiteCoverageTokens.One);
        }
        else
        {
            var rejected = false;
            try
            {
                _ = SiteCoverageNativeTestSupport.ParseSnapshots(receipt, temporary.Root, sources);
            }
            catch (InvalidDataException error)
            {
                rejected = error.Message.Contains(SiteCoverageTokens.UnexpectedProductionScriptFailure, StringComparison.Ordinal);
            }

            await Assert.That(rejected).IsTrue();
        }
    }
}
