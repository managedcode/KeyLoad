namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-009 and AC-BC-027: exercise the analyzer coverage gate through its real PowerShell process.</summary>
[NotInParallel(SiteAnalyzerCoverageTokens.ProcessIsolationKey)]
internal sealed class SiteAnalyzerCoverageProcessTests
{
    [Test]
    public async Task PrepareAndVerifyRetainIntegerEvidenceAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        var prepare = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModePrepare);
        await Assert.That(prepare.ExitCode).IsEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        await Assert.That(File.Exists(scope.ManifestPath)).IsTrue();
        using var manifest = SiteAnalyzerCoverageTestScope.ReadJson(scope.ManifestPath);
        await Assert.That(manifest.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonSourceRevision).GetString())
            .IsEqualTo(SiteAnalyzerCoverageTokens.TriggerRevisionB);
        var runtimeProof = await SiteAnalyzerCoverageProcess.ReadPowerShellRuntimeAsync(scope.Repository);
        var runtimeVersions = runtimeProof.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        await Assert.That(runtimeProof.ExitCode).IsEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        await Assert.That(manifest.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonRuntimeVersion).GetString())
            .IsEqualTo(runtimeVersions[SiteAnalyzerCoverageTokens.RuntimeVersionOutputIndex].Trim());
        await Assert.That(manifest.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonPowerShellVersion).GetString())
            .IsEqualTo(runtimeVersions[SiteAnalyzerCoverageTokens.PowerShellVersionOutputIndex].Trim());
        await Assert.That(manifest.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonSources).GetArrayLength()).IsEqualTo(SiteAnalyzerCoverageTokens.ExpectedSourceCount);
        await Assert.That(manifest.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonConfiguration).GetArrayLength()).IsEqualTo(SiteAnalyzerCoverageTokens.ExpectedConfigurationCount);
        await Assert.That(manifest.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonGateScripts).GetArrayLength()).IsEqualTo(SiteAnalyzerCoverageTokens.ExpectedGateScriptCount);
        await Assert.That(manifest.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonSettingsConfigHash).GetString()!.Length).IsEqualTo(SiteAnalyzerCoverageTokens.HashLength);
        await Assert.That(manifest.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonSettingsConfigPath).GetString())
            .IsEqualTo(Path.GetFullPath(scope.SettingsCopyPath));
        var firstSource = manifest.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonSources)[0];
        await Assert.That(firstSource.GetProperty(SiteAnalyzerCoverageTokens.JsonSha256).GetString()!.Length).IsEqualTo(SiteAnalyzerCoverageTokens.HashLength);
        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.Valid);

        var verify = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(verify.ExitCode).IsEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        using var reportDocument = SiteAnalyzerCoverageTestScope.ReadJson(scope.SummaryPath);
        var report = reportDocument.RootElement;
        await Assert.That(report.TryGetProperty(SiteAnalyzerCoverageTokens.JsonSummaryProperty, out _)).IsTrue();
        await Assert.That(report.TryGetProperty(SiteAnalyzerCoverageTokens.JsonPassedProperty, out _)).IsTrue();
    }

    [Test]
    public async Task UniqueCoberturaSourceRootResolvesWithinCheckoutAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        await scope.PrepareAsync();
        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.UniqueSourceRoot);
        var verify = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(verify.ExitCode).IsEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
    }

    [Test]
    public async Task SourceLinesUnionDistinctClassesAndTreatAnyPositiveHitsAsCoveredAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        await scope.PrepareAsync();
        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.CrossClassLineUnion);
        var verify = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(verify.ExitCode).IsEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        using var report = SiteAnalyzerCoverageTestScope.ReadJson(scope.SummaryPath);
        var module = report.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonSummaryProperty);
        await Assert.That(module.GetProperty(SiteAnalyzerCoverageTokens.JsonLinesValid).GetInt64())
            .IsEqualTo(SiteAnalyzerCoverageTokens.ExpectedUniqueLineCount);
        await Assert.That(module.GetProperty(SiteAnalyzerCoverageTokens.JsonLinesCovered).GetInt64())
            .IsEqualTo(SiteAnalyzerCoverageTokens.ExpectedUniqueLineCount);
    }

    [Test]
    public async Task MethodRecordsDoNotContributeDuplicateCoverageAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        await scope.PrepareAsync();
        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.MethodsExcluded);
        var verify = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(verify.ExitCode).IsEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        using var report = SiteAnalyzerCoverageTestScope.ReadJson(scope.SummaryPath);
        await Assert.That(report.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonSummaryProperty)
            .GetProperty(SiteAnalyzerCoverageTokens.JsonLinesValid).GetInt64())
            .IsEqualTo(SiteAnalyzerCoverageTokens.ExpectedUniqueLineCount);
    }

    [Test]
    public async Task MissingCoverageReportFailsClosedAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        await scope.PrepareAsync();
        var result = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify, includeCoverage: false);
        await Assert.That(result.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        using var failureReport = SiteAnalyzerCoverageTestScope.ReadJson(scope.SummaryPath);
        await Assert.That(failureReport.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonPassedProperty).GetBoolean()).IsFalse();
        await Assert.That(failureReport.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonNativeReportHash).ValueKind)
            .IsEqualTo(System.Text.Json.JsonValueKind.Null);
        await Assert.That(failureReport.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonSummaryProperty).ValueKind)
            .IsEqualTo(System.Text.Json.JsonValueKind.Null);
        await Assert.That(failureReport.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonFiles).GetArrayLength()).IsEqualTo(0);
        await Assert.That(failureReport.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonCriticalPipelines).GetArrayLength()).IsEqualTo(0);
        await Assert.That(failureReport.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonFailuresProperty).GetArrayLength()).IsNotEqualTo(0);
    }

    [Test]
    public async Task MissingManifestRetainsFailureAndNativeReportHashAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.Valid);
        var result = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(result.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        using var failureReport = SiteAnalyzerCoverageTestScope.ReadJson(scope.SummaryPath);
        await Assert.That(failureReport.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonPassedProperty).GetBoolean()).IsFalse();
        await Assert.That(failureReport.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonNativeReportHash).GetString()!.Length)
            .IsEqualTo(SiteAnalyzerCoverageTokens.HashLength);
    }

    [Test]
    public async Task DtdAndMalformedIntegerInputsFailClosedAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        await scope.PrepareAsync();
        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.Dtd);
        var dtd = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(dtd.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);

        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.MalformedInteger);
        var malformed = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(malformed.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
    }

    [Test]
    public async Task UnsafeAndConflictingPathsFailClosedAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        await scope.PrepareAsync();
        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.UnsafePath);
        var unsafePath = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(unsafePath.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);

        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.ParentSourceRoot);
        var parentSourceRoot = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(parentSourceRoot.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);

        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.MultipleSourceRoots);
        var multipleSourceRoots = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(multipleSourceRoots.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);

        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.ConflictingLine);
        var conflict = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(conflict.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);

        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.ConflictingBranch);
        var branchConflict = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(branchConflict.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
    }

    [Test]
    public async Task ThresholdFailureStillWritesDerivedReportAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        await scope.PrepareAsync();
        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.BelowThreshold);
        var verify = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(verify.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        await Assert.That(File.Exists(scope.SummaryPath)).IsTrue();
    }

    [Test]
    public async Task EmptyBranchDenominatorFailsClosedAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        await scope.PrepareAsync();
        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.EmptyBranches);
        var verify = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(verify.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);

        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.ZeroBranchDenominator);
        var zero = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(zero.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
    }

}
