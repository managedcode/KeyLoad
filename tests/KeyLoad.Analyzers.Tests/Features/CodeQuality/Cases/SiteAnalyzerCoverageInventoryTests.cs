namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-009 and AC-BC-027: prove complete analyzer source and configuration inventories.</summary>
[NotInParallel(SiteAnalyzerCoverageTokens.ProcessIsolationKey)]
internal sealed class SiteAnalyzerCoverageInventoryTests
{
    [Test]
    public async Task MissingModuleAndMissingExecutableSourceFailClosedAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        await scope.PrepareAsync();
        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.MissingModule);
        var missingModule = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(missingModule.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);

        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.MissingSource);
        var missingSource = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(missingSource.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);

        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.UnknownSource);
        var unknownSource = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(unknownSource.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
    }

    [Test]
    public async Task SourceChangeAfterPrepareFailsClosedAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope(copyRepository: true);
        await scope.PrepareAsync();
        await File.AppendAllTextAsync(Path.Combine(scope.Repository, SiteAnalyzerCoverageTokens.HashFileRelativePath), SiteAnalyzerCoverageTokens.ChangedSourceContent);
        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.Valid);
        var verify = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(verify.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        await Assert.That(File.Exists(scope.SummaryPath)).IsTrue();
    }

    [Test]
    public async Task GateHelperChangeAfterPrepareFailsClosedAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope(copyRepository: true);
        await scope.PrepareAsync();
        await File.AppendAllTextAsync(
            Path.Combine(scope.Repository, SiteAnalyzerCoverageTokens.GateHelperRelativePath),
            SiteAnalyzerCoverageTokens.ChangedSourceContent);
        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.Valid);
        var verify = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(verify.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
    }

    [Test]
    public async Task CollectorSettingsCopyChangeAfterPrepareFailsClosedAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        await scope.PrepareAsync();
        await File.AppendAllTextAsync(scope.SettingsCopyPath, SiteAnalyzerCoverageTokens.ChangedSourceContent);
        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.Valid);
        var verify = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(verify.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
    }

    [Test]
    public async Task StaleManifestOrReportPreventsPrepareAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        await File.WriteAllTextAsync(scope.ManifestPath, SiteAnalyzerCoverageTokens.StaleEvidenceMarker);
        var prepare = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModePrepare);
        await Assert.That(prepare.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);

        using var staleReportScope = new SiteAnalyzerCoverageTestScope();
        await File.WriteAllTextAsync(staleReportScope.SummaryPath, SiteAnalyzerCoverageTokens.StaleEvidenceMarker);
        var staleReportPrepare = await staleReportScope.RunAsync(SiteAnalyzerCoverageTokens.ModePrepare);
        await Assert.That(staleReportPrepare.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
    }

    [Test]
    public async Task ConfigurationInventoryChangeFailsClosedAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope(copyRepository: true);
        await scope.PrepareAsync();
        File.Delete(Path.Combine(scope.Repository, SiteAnalyzerCoverageTokens.ConfigFileRelativePath));
        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.Valid);
        var verify = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify);
        await Assert.That(verify.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
    }

    [Test]
    public async Task SourceDirectoryMustExactlyMatchFrozenInventoryAsync()
    {
        using var missingScope = new SiteAnalyzerCoverageTestScope(copyRepository: true);
        File.Delete(Path.Combine(missingScope.Repository, SiteAnalyzerCoverageTokens.HashFileRelativePath));
        var missing = await missingScope.RunAsync(SiteAnalyzerCoverageTokens.ModePrepare);
        await Assert.That(missing.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);

        using var unexpectedScope = new SiteAnalyzerCoverageTestScope(copyRepository: true);
        var referenceSource = Path.Combine(unexpectedScope.Repository, SiteAnalyzerCoverageTokens.HashFileRelativePath);
        var extraSource = Path.Combine(Path.GetDirectoryName(referenceSource)!, SiteAnalyzerCoverageTokens.UnexpectedSourceFileName);
        await File.WriteAllTextAsync(extraSource, SiteAnalyzerCoverageTokens.ChangedSourceContent);
        var unexpected = await unexpectedScope.RunAsync(SiteAnalyzerCoverageTokens.ModePrepare);
        await Assert.That(unexpected.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
    }
}
