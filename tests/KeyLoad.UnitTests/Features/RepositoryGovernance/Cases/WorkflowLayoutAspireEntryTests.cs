using KeyLoad.UnitTests.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.RepositoryGovernance;

internal sealed class WorkflowLayoutAspireEntryTests
{
    private const string AppHostCommand = "dotnet run --project src/KeyLoad.AppHost";

    [Test]
    public async Task AcTest009CiPreservesEveryRequiredAspireSuiteAndScalarRunnerSetting()
    {
        var ci = WorkflowLayoutSource.Read("ci.yml");
        await AssertSuiteAsync(ci, "analyzer-rules", "analyzers");
        await AssertSuiteAsync(ci, "verify", "analyzers");
        await AssertSuiteAsync(ci, "verify", "unit");
        await AssertSuiteAsync(ci, "verify", "unit-scalar");
        await AssertSuiteAsync(ci, "verify", "recovery");
        await AssertSuiteAsync(ci, "docker-rf3", "rf3");
        await AssertScalarRunnerSelectionAsync();
        await AssertSuiteProjectsAsync();
    }

    [Test]
    public async Task AcTest010WorkflowAndCompositeTestLaunchesUseAspireAndBoundedComparisonFilters()
    {
        await AssertNoDirectTestCommandsAsync();
        await AssertImageTestLaunchesAsync();
        var benchmarks = WorkflowLayoutSource.Read("benchmarks.yml");
        var entry = ReadRepositoryFile("scripts/Features/BenchmarkComparisons/run-workload.mjs");
        foreach (var jobId in WorkflowDatabaseGroups.JobIds)
        {
            var job = WorkflowLayoutSource.JobBlock(benchmarks, jobId);
            await Assert.That(job.Contains("node scripts/Features/BenchmarkComparisons/run-workload.mjs", StringComparison.Ordinal)).IsTrue();
        }
        await Assert.That(entry.Contains("'run', '--project', 'src/KeyLoad.AppHost', '--no-build', '--no-restore', '--configuration', 'Release'", StringComparison.Ordinal)).IsTrue();
        await Assert.That(entry.Contains("--KeyLoadTests:Suite=comparison", StringComparison.Ordinal)).IsTrue();
        await Assert.That(entry.Contains("--KeyLoadTests:Filter=/*/*/IsolatedNativeComparisonTests/*", StringComparison.Ordinal)).IsTrue();
        await Assert.That(entry.Contains("--KeyLoadTests:TimeoutMinutes=140", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task AcTest011SiteQualificationKeepsAppHostBuildTrxCoverageAndExplicitEvidencePaths()
    {
        var action = ReadRepositoryFile(".github/workflows/Features/BenchmarkComparisons/QualifySite/action.yml");
        var appHostBuild = action.IndexOf("dotnet build src/KeyLoad.AppHost/KeyLoad.AppHost.csproj",
            StringComparison.Ordinal);
        var firstSuiteLaunch = action.IndexOf(AppHostCommand, StringComparison.Ordinal);
        await Assert.That(appHostBuild).IsGreaterThan(-1);
        await Assert.That(firstSuiteLaunch).IsGreaterThan(appHostBuild);
        var steps = WorkflowStepNameTests.StepBlocks(action);
        var analyzer = steps.Single(step => step.Contains("Build and test code analyzers", StringComparison.Ordinal));
        var site = steps.Single(step => step.Contains("Run all website tests", StringComparison.Ordinal));
        await AssertStartupEvidenceAsync(analyzer);
        await AssertAnalyzerEvidenceAsync(analyzer);
        await AssertSiteEvidenceAsync(site);
        var receipts = steps.Single(step => step.Contains("Check every required test passed", StringComparison.Ordinal));
        await Assert.That(receipts.Contains("@('startup-tests', 'selection-tests', 'analyzer-tests', 'site-tests')", StringComparison.Ordinal)).IsTrue();
        await Assert.That(receipts.Contains("$env:EVIDENCE_DIR/$suite", StringComparison.Ordinal)).IsTrue();
        await Assert.That(receipts.Contains("[int]$counts.executed -ne [int]$counts.total", StringComparison.Ordinal)).IsTrue();
        await Assert.That(receipts.Contains("[int]$counts.passed -ne [int]$counts.total", StringComparison.Ordinal)).IsTrue();
        await Assert.That(receipts.Contains("[int]$counts.total -le 0", StringComparison.Ordinal)).IsTrue();
        await Assert.That(receipts.Contains("$files.Count -ne 1", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertStartupEvidenceAsync(string analyzer)
    {
        foreach (var required in new[]
        {
            "dotnet build tests/KeyLoad.UnitTests/KeyLoad.UnitTests.csproj --no-restore --configuration Release",
            AppHostCommand, "--KeyLoadTests:Suite=unit", "--KeyLoadTests:ReportTrx=true",
            "--KeyLoadTests:Filter=/*/*/SiteQualificationStartupTests/*",
            "--KeyLoadTests:ResultsDirectory=$EVIDENCE_DIR/startup-tests"
        })
        {
            await Assert.That(analyzer.Contains(required, StringComparison.Ordinal)).IsTrue();
        }

        var startup = analyzer.IndexOf("--KeyLoadTests:Suite=unit", StringComparison.Ordinal);
        var native = analyzer.IndexOf("--KeyLoadTests:Suite=analyzers", StringComparison.Ordinal);
        await Assert.That(startup).IsLessThan(native);
        await Assert.That(analyzer.Contains("continue-on-error", StringComparison.Ordinal)).IsFalse();
    }

    private static async Task AssertAnalyzerEvidenceAsync(string analyzer)
    {
        foreach (var required in new[]
        {
            AppHostCommand, "--KeyLoadTests:Suite=analyzers", "--KeyLoadTests:ReportTrx=true",
            "--KeyLoadTests:ResultsDirectory=$EVIDENCE_DIR/analyzer-tests",
            "--KeyLoadTests:CoverageSettings=$EVIDENCE_DIR/analyzer-coverage/coverage.config.xml",
            "--KeyLoadTests:CoverageOutput=$EVIDENCE_DIR/analyzer-coverage/coverage.cobertura.xml",
            "coverage.cobertura.xml"
        })
        {
            await Assert.That(analyzer.Contains(required, StringComparison.Ordinal)).IsTrue();
        }
    }

    private static async Task AssertSiteEvidenceAsync(string site)
    {
        foreach (var required in new[]
        {
            AppHostCommand, "--KeyLoadTests:Suite=site", "--KeyLoadTests:ReportTrx=true",
            "--KeyLoadTests:ResultsDirectory=$EVIDENCE_DIR/site-tests"
        })
        {
            await Assert.That(site.Contains(required, StringComparison.Ordinal)).IsTrue();
        }
    }

    private static async Task AssertSuiteAsync(string workflow, string jobId, string suite)
    {
        var job = WorkflowLayoutSource.JobBlock(workflow, jobId);
        await Assert.That(job.Contains(AppHostCommand, StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("--KeyLoadTests:Suite=" + suite, StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertScalarRunnerSelectionAsync()
    {
        var source = ReadRepositoryFile("src/KeyLoad.AppHost/Features/TestInfrastructure/Hosting/TestSuiteResources.cs");
        var runner = source.IndexOf("var runner = builder.AddExecutable(settings.ResourceName", StringComparison.Ordinal);
        var scalarCondition = source.IndexOf("if (settings.Suite == \"unit-scalar\")", StringComparison.Ordinal);
        await Assert.That(runner).IsGreaterThan(-1);
        await Assert.That(scalarCondition).IsGreaterThan(runner);
        await Assert.That(source[scalarCondition..].Contains(
            "runner.WithEnvironment(\"DOTNET_EnableHWIntrinsic\", \"0\")", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertSuiteProjectsAsync()
    {
        var source = ReadRepositoryFile("src/KeyLoad.AppHost/Features/TestInfrastructure/Execution/TestSuiteSettings.cs");
        foreach (var mapping in new[]
        {
            "\"analyzers\" => \"KeyLoad.Analyzers.Tests\"",
            "\"unit\" or \"unit-scalar\" => \"KeyLoad.UnitTests\"",
            "\"recovery\" => \"KeyLoad.RecoveryTests\"",
            "\"rf3\" => \"KeyLoad.IntegrationTests\""
        })
        {
            await Assert.That(source.Contains(mapping, StringComparison.Ordinal)).IsTrue();
        }
    }

    private static async Task AssertNoDirectTestCommandsAsync()
    {
        var root = IsolatedAggregateNodeProcess.RepositoryRoot();
        var directory = Path.Combine(root, ".github", "workflows");
        var sources = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(static path => Path.GetExtension(path) is ".yml" or ".yaml");
        foreach (var path in sources)
        {
            var source = await File.ReadAllTextAsync(path, TestContext.Current!.Execution.CancellationToken);
            await Assert.That(source.Contains("dotnet test", StringComparison.Ordinal)).IsFalse();
        }
    }

    private static async Task AssertImageTestLaunchesAsync()
    {
        var benchmarks = WorkflowLayoutSource.Read("benchmarks.yml");
        var imageJob = WorkflowLayoutSource.JobBlock(benchmarks, "comparison-images");
        var steps = WorkflowStepNameTests.StepBlocks(imageJob)
            .Where(static step => step.Contains("Suite=comparison", StringComparison.Ordinal)).ToArray();
        var expectedFilters = new[]
        {
            "/*/*/TimeSeriesIntensivePinnedImageTests/*", "/*/*/ImageBundleRealTests/*",
            "/*/*/IsolatedGitHubCurrentJobTests/*", "/*/*/Isolated*Resource*/*",
            "/*/*/MongoReadinessResourceTests/*", "/*/*/MongoReadinessIdentityTests/*",
            "/*/*/MongoReadinessTaskFailureTests/*", "/*/*/IsolatedKurrentVolumeRegressionFailureTests/*",
            "/*/*/IsolatedKeyLoadAdmissionTests/*", "/*/*/ComparisonResourceLogBufferTests/*",
            "/*/*/ComparisonProgress*/*",
            "/*/*/IsolatedKeyLoadReplayAdmissionTests/*", "/*/*/RedisReplicaDiagnosticTests/*",
            "/*/*/MongoBootstrapDiagnosticTests/*", "/*/*/KurrentCleanupDiagnosticTests/*",
            "/*/*/KurrentStreamOwnershipTests/*", "/*/*/ComparisonReplayDiagnosticLogTests/*",
            "/*/*/ComparisonReplayDiagnosticRetentionTests/*", "/*/*/TimeSeriesWorkloadTests/*",
            "/*/*/TimeSeriesPackageVersionTests/*", "/*/*/IsolatedTimeSeriesKeyLoadResourceTests/*",
            "/*/*/IsolatedTimeSeriesTimescaleResourceTests/*",
            "/*/*/IsolatedTimeSeriesBenchmarkResourceTests/*",
            "/*/*/IsolatedKurrentDiscoverySettingsTests/*",
            "/*/*/OpenSearchVectorQueryTests/*", "/*/*/OpenSearchVectorResponseTests/*"
        };
        await Assert.That(steps.Length).IsEqualTo(expectedFilters.Length);
        foreach (var step in steps)
        {
            await Assert.That(step.Contains(AppHostCommand, StringComparison.Ordinal)).IsTrue();
            await Assert.That(step.Contains("--KeyLoadTests:Filter=", StringComparison.Ordinal)).IsTrue();
            await Assert.That(step.Contains("--KeyLoadTests:ResultsDirectory=", StringComparison.Ordinal)).IsTrue();
        }
        foreach (var filter in expectedFilters)
        {
            await Assert.That(imageJob.Contains("--KeyLoadTests:Filter=" + filter, StringComparison.Ordinal)).IsTrue();
        }
    }

    private static string ReadRepositoryFile(string relativePath)
        => File.ReadAllText(Path.Combine(IsolatedAggregateNodeProcess.RepositoryRoot(), relativePath));
}
