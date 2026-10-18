namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-028: site native coverage is tied to its selected source checkout.</summary>
[NotInParallel(SiteAnalyzerCoverageTokens.ProcessIsolationKey)]
internal sealed class SiteAnalyzerSourceRevisionTests
{
    [Test]
    public async Task DedicatedRevisionWinsOverTriggerForManifestAndReportAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        var environment = CreateRevisionEnvironment(SiteAnalyzerCoverageTokens.SiteRevisionA, SiteAnalyzerCoverageTokens.TriggerRevisionB);
        var prepare = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModePrepare, environmentOverrides: environment);
        await Assert.That(prepare.ExitCode).IsEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        using var manifest = SiteAnalyzerCoverageTestScope.ReadJson(scope.ManifestPath);
        await Assert.That(ReadRevision(manifest)).IsEqualTo(SiteAnalyzerCoverageTokens.SiteRevisionA);

        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.Valid);
        var verify = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify, environmentOverrides: environment);
        await Assert.That(verify.ExitCode).IsEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        using var report = SiteAnalyzerCoverageTestScope.ReadJson(scope.SummaryPath);
        await Assert.That(ReadRevision(report)).IsEqualTo(SiteAnalyzerCoverageTokens.SiteRevisionA);
    }

    [Test]
    public async Task AbsentDedicatedRevisionKeepsLegacyGitHubShaCallerAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        var environment = CreateRevisionEnvironment(null, SiteAnalyzerCoverageTokens.TriggerRevisionB);
        var prepare = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModePrepare, environmentOverrides: environment);
        await Assert.That(prepare.ExitCode).IsEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        using var manifest = SiteAnalyzerCoverageTestScope.ReadJson(scope.ManifestPath);
        await Assert.That(ReadRevision(manifest)).IsEqualTo(SiteAnalyzerCoverageTokens.TriggerRevisionB);

        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.Valid);
        var verify = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify, environmentOverrides: environment);
        await Assert.That(verify.ExitCode).IsEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        using var report = SiteAnalyzerCoverageTestScope.ReadJson(scope.SummaryPath);
        await Assert.That(ReadRevision(report)).IsEqualTo(SiteAnalyzerCoverageTokens.TriggerRevisionB);
    }

    [Test]
    public async Task PresentInvalidDedicatedRevisionFailsWithoutTriggerFallbackAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        var valid = CreateRevisionEnvironment(SiteAnalyzerCoverageTokens.SiteRevisionA, SiteAnalyzerCoverageTokens.TriggerRevisionB);
        var prepare = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModePrepare, environmentOverrides: valid);
        await Assert.That(prepare.ExitCode).IsEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.Valid);

        var invalid = CreateRevisionEnvironment(SiteAnalyzerCoverageTokens.EmptyRevision, SiteAnalyzerCoverageTokens.TriggerRevisionB);
        var verify = await scope.RunAsync(SiteAnalyzerCoverageTokens.ModeVerify, environmentOverrides: invalid);
        await Assert.That(verify.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        await Assert.That(verify.StandardError.Contains(SiteAnalyzerCoverageTokens.ErrorNoRevision, StringComparison.Ordinal)).IsTrue();
        using var report = SiteAnalyzerCoverageTestScope.ReadJson(scope.SummaryPath);
        await Assert.That(report.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonSourceRevision).ValueKind)
            .IsEqualTo(System.Text.Json.JsonValueKind.Null);
        await Assert.That(report.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonFailuresProperty).GetArrayLength()).IsNotEqualTo(0);
    }

    [Test]
    public async Task WhitespaceUppercaseAndMalformedDedicatedRevisionsFailPrepareAsync()
    {
        foreach (var invalidRevision in SiteAnalyzerCoverageTokens.InvalidDedicatedRevisions)
        {
            await AssertInvalidDedicatedRevisionFailsPrepareAsync(invalidRevision);
        }
    }

    [Test]
    public async Task ChangedDedicatedRevisionBetweenPrepareAndVerifyFailsAsync()
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        var prepare = await scope.RunAsync(
            SiteAnalyzerCoverageTokens.ModePrepare,
            environmentOverrides: CreateRevisionEnvironment(SiteAnalyzerCoverageTokens.SiteRevisionA, SiteAnalyzerCoverageTokens.TriggerRevisionB));
        await Assert.That(prepare.ExitCode).IsEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        await scope.WriteFixtureAsync(SiteAnalyzerCoverageFixture.Valid);

        var verify = await scope.RunAsync(
            SiteAnalyzerCoverageTokens.ModeVerify,
            environmentOverrides: CreateRevisionEnvironment(SiteAnalyzerCoverageTokens.TriggerRevisionB, SiteAnalyzerCoverageTokens.TriggerRevisionB));
        await Assert.That(verify.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        await Assert.That(verify.StandardError.Contains(SiteAnalyzerCoverageTokens.ErrorInvalidManifest, StringComparison.Ordinal)).IsTrue();
        using var report = SiteAnalyzerCoverageTestScope.ReadJson(scope.SummaryPath);
        await Assert.That(ReadRevision(report)).IsEqualTo(SiteAnalyzerCoverageTokens.TriggerRevisionB);
        await Assert.That(report.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonFailuresProperty).GetArrayLength()).IsNotEqualTo(0);
    }

    [Test]
    public async Task MissingOrInvalidRevisionSourcesFailPrepareAsync()
    {
        using var missingScope = new SiteAnalyzerCoverageTestScope();
        var missing = await missingScope.RunAsync(
            SiteAnalyzerCoverageTokens.ModePrepare,
            environmentOverrides: CreateRevisionEnvironment(null, null));
        await Assert.That(missing.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        await Assert.That(missing.StandardError.Contains(SiteAnalyzerCoverageTokens.ErrorNoRevision, StringComparison.Ordinal)).IsTrue();
        await Assert.That(File.Exists(missingScope.ManifestPath)).IsFalse();

        using var invalidScope = new SiteAnalyzerCoverageTestScope();
        var invalid = await invalidScope.RunAsync(
            SiteAnalyzerCoverageTokens.ModePrepare,
            environmentOverrides: CreateRevisionEnvironment(null, SiteAnalyzerCoverageTokens.UppercaseRevision));
        await Assert.That(invalid.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        await Assert.That(invalid.StandardError.Contains(SiteAnalyzerCoverageTokens.ErrorNoRevision, StringComparison.Ordinal)).IsTrue();
        await Assert.That(File.Exists(invalidScope.ManifestPath)).IsFalse();

        using var malformedScope = new SiteAnalyzerCoverageTestScope();
        var malformed = await malformedScope.RunAsync(
            SiteAnalyzerCoverageTokens.ModePrepare,
            environmentOverrides: CreateRevisionEnvironment(SiteAnalyzerCoverageTokens.MalformedRevision, SiteAnalyzerCoverageTokens.TriggerRevisionB));
        await Assert.That(malformed.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        await Assert.That(malformed.StandardError.Contains(SiteAnalyzerCoverageTokens.ErrorNoRevision, StringComparison.Ordinal)).IsTrue();
        await Assert.That(File.Exists(malformedScope.ManifestPath)).IsFalse();
    }

    private static async Task AssertInvalidDedicatedRevisionFailsPrepareAsync(string invalidRevision)
    {
        using var scope = new SiteAnalyzerCoverageTestScope();
        var result = await scope.RunAsync(
            SiteAnalyzerCoverageTokens.ModePrepare,
            environmentOverrides: CreateRevisionEnvironment(invalidRevision, SiteAnalyzerCoverageTokens.TriggerRevisionB));
        await Assert.That(result.ExitCode).IsNotEqualTo(SiteAnalyzerCoverageTokens.SuccessExitCode);
        await Assert.That(result.StandardError.Contains(SiteAnalyzerCoverageTokens.ErrorNoRevision, StringComparison.Ordinal)).IsTrue();
        await Assert.That(File.Exists(scope.ManifestPath)).IsFalse();
    }

    private static Dictionary<string, string?> CreateRevisionEnvironment(string? dedicatedRevision, string? triggerRevision) =>
        new()
        {
            [SiteAnalyzerCoverageTokens.SiteRevisionVariable] = dedicatedRevision,
            [SiteAnalyzerCoverageTokens.GitHubRevisionVariable] = triggerRevision
        };

    private static string? ReadRevision(System.Text.Json.JsonDocument document) =>
        document.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonSourceRevision).ValueKind == System.Text.Json.JsonValueKind.Null
            ? null
            : document.RootElement.GetProperty(SiteAnalyzerCoverageTokens.JsonSourceRevision).GetString();
}
