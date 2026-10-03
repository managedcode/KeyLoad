using KeyLoad.UnitTests.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.RepositoryGovernance;

internal sealed class WorkflowLayoutCurrentSiteTests
{
    private const string ActionRoot = ".github/workflows/Features/BenchmarkComparisons";
    private const string RetiredCollector = "collect-github-evidence.sh";
    private const string RetiredReports = "KEYLOAD_SITE_REPORTS";
    private const string Capture = "site-isolated-github-cli.mjs capture";
    private const string Freshness = "site-isolated-github-cli.mjs fresh";
    private const string Deployment = "uses: actions/deploy-pages@";
    private const string FeatureRoot = "site/Features/BenchmarkComparisons";
    private const string ThinBuilder = "site/scripts/build.mjs";
    private const string Closure = "scripts/Features/BenchmarkComparisons/site-isolated-dependencies.txt";
    private const int ClosureCount = 70;

    [Test]
    public async Task AcBcCurrent003TrustedClosureIncludesEveryActualBuilderAndBrowserConsumer()
    {
        var root = IsolatedAggregateNodeProcess.RepositoryRoot();
        var closure = await File.ReadAllLinesAsync(Path.Combine(root, Closure));
        await Assert.That(closure.Length).IsEqualTo(ClosureCount);
        await Assert.That(closure.SequenceEqual(closure.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)))
            .IsTrue();
        await Assert.That(closure.All(path => File.Exists(Path.Combine(root, path)))).IsTrue();
        var actual = Directory.EnumerateFiles(Path.Combine(root, FeatureRoot), "*.mjs")
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Append(ThinBuilder).Order(StringComparer.Ordinal);
        var expected = closure.Where(path => path.StartsWith(FeatureRoot + "/", StringComparison.Ordinal)
            || path == ThinBuilder).Order(StringComparer.Ordinal);
        await Assert.That(actual.SequenceEqual(expected)).IsTrue();
    }

    [Test]
    public async Task AcBcCurrent001002QualificationUsesCompleteCurrentAuthorityAndAspireTests()
    {
        var action = ReadAction("QualifySite");
        await Assert.That(action.Contains(Capture, StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains("metadata-proof.json", StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains("archive-receipt.json", StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains("site-isolated-dependencies.txt", StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains("cmp \"control/$relative\" \"website/$relative\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains("--KeyLoadTests:Suite=site", StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains("--KeyLoadTests:Suite=analyzers", StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains("counts.passed -ne [int]$counts.total", StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains(RetiredCollector, StringComparison.Ordinal)).IsFalse();
        await Assert.That(action.Contains(RetiredReports, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task AcBcCurrent003FinalBuilderKeepsBoundedImmutableOriginalInputsAndCompleteReceipts()
    {
        var action = ReadAction("BuildIsolatedSite");
        var before = action.IndexOf("verify_isolated_inputs before", StringComparison.Ordinal);
        var builder = action.IndexOf("node site/scripts/build.mjs --isolated=", StringComparison.Ordinal);
        var after = action.IndexOf("verify_isolated_inputs after", StringComparison.Ordinal);
        await Assert.That(before >= 0 && before < builder && builder < after).IsTrue();
        foreach (var contract in new[]
        {
            "isolated_receipt_limit=4194304", "chmod 400", "verify_authority",
            "result.workers == 270 and .result.files == 277", "schemaVersion:2",
            "source:$isolated[0].source", "tests:$tests[0]", "isolated:$isolated[0]",
            "nativeReportSha256:$native_hash", "siteReportSha256:$site_hash",
            "cmp \"$KEYLOAD_SITE_ISOLATED_AGGREGATE/aggregate.json\"",
        })
        {
            await Assert.That(action.Contains(contract, StringComparison.Ordinal)).IsTrue();
        }
        await Assert.That(action.Contains(RetiredReports, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task AcBcCurrent004FreshExactProducerIsRequiredBeforeActualPagesDelivery()
    {
        var action = ReadAction("DeploySite");
        var freshness = action.IndexOf(Freshness, StringComparison.Ordinal);
        var deployment = action.IndexOf(Deployment, StringComparison.Ordinal);
        await Assert.That(freshness >= 0 && freshness < deployment).IsTrue();
        await Assert.That(action.Contains("metadata-proof.json", StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains("archive-receipt.json", StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains("predeploy-isolated-proof.json", StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains("git/ref/heads/main", StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains("deployment-receipt.json", StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains(RetiredCollector, StringComparison.Ordinal)).IsFalse();
    }

    private static string ReadAction(string name) => File.ReadAllText(Path.Combine(
        IsolatedAggregateNodeProcess.RepositoryRoot(), ActionRoot, name, "action.yml"));
}
