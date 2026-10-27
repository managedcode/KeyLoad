namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal sealed class SiteIsolatedCoverageInventoryTests
{
    private const string IsolatedPath = SitePublicationTokens.EvidenceToolsPrefix + "site-isolated-github-controlled.mjs";

    [Test]
    public async Task AC_ISO_009_ActualSourceClosureMatchesTheExecutedDependencyInventory()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var repository = SiteTestInputs.Read().Repository;
        var closure = Path.Combine(repository, SitePublicationTokens.EvidenceToolsPrefix, "site-isolated-dependencies.txt");
        var original = await File.ReadAllBytesAsync(closure, token);
        var start = SiteIsolatedGitHubNodeProcess.CreateStart(repository);
        start.ArgumentList.Add(Path.Combine(repository, SitePublicationTokens.EvidenceToolsPrefix, "session-source-closure.mjs"));
        start.ArgumentList.Add("--verify=" + closure);
        var execution = await SiteIsolatedGitHubProcess.RunAsync(start, token);
        await Assert.That(execution.ExitCode).IsEqualTo(0);
        await Assert.That(execution.StandardError).IsEmpty();
        using var result = System.Text.Json.JsonDocument.Parse(execution.StandardOutput);
        await Assert.That(result.RootElement.GetProperty(SiteIsolatedFields.Files).GetInt32())
            .IsEqualTo(SiteCoverageSourceInventory.ProductionSources.Length);
        await Assert.That((await File.ReadAllBytesAsync(closure, token)).SequenceEqual(original)).IsTrue();
    }

    [Test]
    [Arguments(IsolatedPath, true)]
    [Arguments(IsolatedPath, false)]
    public async Task AC_ISO_009_CoverageRequiresCurrentEvidenceToolInventory(string relative, bool inventoried)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = new SiteCoverageFixtureDirectory();
        await temporary.CreateAsync(token);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(temporary.Root, relative))!);
        var bytes = await temporary.WriteSourceAsync(relative, SiteCoverageTestSupport.FixtureSource, token);
        var receipt = await SiteCoverageTestSupport.RunNodeCoverageForAsync(temporary,
            SiteCoverageTestSupport.TrueArgument, relative, token);
        var sources = new Dictionary<string, SiteCoverageSourceEntry>(StringComparer.Ordinal);
        if (inventoried)
        {
            sources.Add(relative, new(relative, SiteCoverageSourceManifestWriter.Hash(bytes),
                SiteCoverageTestSupport.FixtureSource.Length));
        }

        if (inventoried)
        {
            var snapshots = SiteCoverageTestSupport.ParseSnapshots(receipt, temporary.Root, sources);
            await Assert.That(snapshots.Count).IsEqualTo(SiteCoverageTokens.One);
        }
        else
        {
            var rejected = false;
            try
            {
                _ = SiteCoverageTestSupport.ParseSnapshots(receipt, temporary.Root, sources);
            }
            catch (InvalidDataException error)
            {
                rejected = error.Message.Contains(SiteCoverageTokens.UnexpectedProductionScriptFailure, StringComparison.Ordinal);
            }

            await Assert.That(rejected).IsTrue();
        }
    }
}
