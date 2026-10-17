namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-BC-FAIL-011: actual qualification Bash startup and filesystem collision behavior.</summary>
internal sealed class SiteQualificationStartupTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    public async Task AC_BC_FAIL_011_RealDirectoriesPermitEnvelopeRedirection(int scenario)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var fixture = new SiteQualificationStartupFixture(scenario);
        var result = await fixture.RunAsync(token);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Output).IsEqualTo(string.Empty);
        await Assert.That(result.Error).IsEqualTo(string.Empty);
        await Assert.That(Directory.Exists(fixture.Base)).IsTrue();
        await Assert.That(File.Exists(fixture.Envelope)).IsTrue();
        await Assert.That(new FileInfo(fixture.Envelope).Length).IsEqualTo(0L);
        await Assert.That(Directory.Exists(fixture.CaptureDirectory)).IsFalse();
        await Assert.That(await File.ReadAllTextAsync(fixture.EnvironmentFile, token))
            .IsEqualTo(fixture.ExpectedEnvironment());
        if (scenario == 1)
        {
            await Assert.That(await File.ReadAllTextAsync(fixture.ExistingSentinel, token))
                .IsEqualTo(SiteQualificationStartupFixture.Sentinel);
        }
    }

    [Test]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task AC_BC_FAIL_011_CollisionsRejectBeforeEnvironmentOutputOrTargetChanges(int scenario)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var fixture = new SiteQualificationStartupFixture(scenario);
        var result = await fixture.RunAsync(token);
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(result.Output).IsEqualTo(string.Empty);
        await Assert.That(await File.ReadAllTextAsync(fixture.EnvironmentFile, token))
            .IsEqualTo(SiteQualificationStartupFixture.Sentinel);
        await Assert.That(File.Exists(fixture.Envelope)).IsFalse();
        await Assert.That(Directory.Exists(fixture.CaptureDirectory)).IsFalse();
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(fixture.Target, "sentinel"), token))
            .IsEqualTo(SiteQualificationStartupFixture.Sentinel);
        await Assert.That(Directory.GetFileSystemEntries(fixture.Target).Length).IsEqualTo(1);
        if (scenario < 4)
        {
            var collision = scenario == 2 ? fixture.Base : fixture.Parent;
            await Assert.That(await File.ReadAllTextAsync(collision, token))
                .IsEqualTo(SiteQualificationStartupFixture.Sentinel);
        }
        else
        {
            await Assert.That(fixture.LinkTarget).IsEqualTo(fixture.Target);
        }
    }
}
