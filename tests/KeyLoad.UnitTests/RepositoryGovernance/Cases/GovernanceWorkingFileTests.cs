namespace KeyLoad.UnitTests.Features.RepositoryGovernance;

/// <summary>AC-MCAF-009: the actual validator rejects transient working files at any depth.</summary>
internal sealed class GovernanceWorkingFileTests
{
    private const int SuccessExit = 0;
    private const int FailureExit = 1;
    private const string PlanSuffix = ".plan.md";
    private const string BrainstormSuffix = ".brainstorm.md";
    private const string AcceptanceSuffix = ".acceptance.md";
    private const string RootFileName = "temporary";
    private const string NestedFileName = "nested/more/temporary";
    private const string DraftContent = "working draft";
    private const string RootPolicyPath = "AGENTS.md";
    private const string ProjectPath = "src/KeyLoad.Abstractions/KeyLoad.Abstractions.csproj";
    private const string SuccessMarker = "PASS: preserved root prefix";
    private const string WorkingFileMarker = "Working planning file is present: ";
    private const string PrefixFailureMarker = "preserved prefix SHA256 mismatch";
    private const string InventoryFailureMarker = "Project inventory is missing: ";

    [Test]
    public async Task CanonicalGovernanceFilesPassWithoutWorkingFiles()
    {
        using var fixture = await GovernanceRootFixture.CreateAsync(TestContext.Current!.Execution.CancellationToken);
        var result = await GovernanceNodeProcess.RunAsync(fixture.Root);
        await Assert.That(result.ExitCode).IsEqualTo(SuccessExit).Because(result.Error);
        await Assert.That(result.Output).Contains(SuccessMarker);
        await Assert.That(result.Error).IsEmpty();
    }

    [Test]
    [Arguments(PlanSuffix, false)]
    [Arguments(BrainstormSuffix, false)]
    [Arguments(AcceptanceSuffix, false)]
    [Arguments(PlanSuffix, true)]
    [Arguments(BrainstormSuffix, true)]
    [Arguments(AcceptanceSuffix, true)]
    public async Task WorkingFileFailsAtRootOrNestedDirectory(string suffix, bool nested)
    {
        using var fixture = await GovernanceRootFixture.CreateAsync(TestContext.Current!.Execution.CancellationToken);
        var path = $"{(nested ? NestedFileName : RootFileName)}{suffix}";
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.PathFor(path))!);
        await File.WriteAllTextAsync(fixture.PathFor(path), DraftContent,
            TestContext.Current!.Execution.CancellationToken);
        var result = await GovernanceNodeProcess.RunAsync(fixture.Root);
        await Assert.That(result.ExitCode).IsEqualTo(FailureExit);
        await Assert.That(result.Error).Contains($"{WorkingFileMarker}{path}");
    }

    [Test]
    public async Task ModifiedPreservedRootPrefixStillFails()
    {
        using var fixture = await GovernanceRootFixture.CreateAsync(TestContext.Current!.Execution.CancellationToken);
        var policy = fixture.PathFor(RootPolicyPath);
        var bytes = await File.ReadAllBytesAsync(policy, TestContext.Current!.Execution.CancellationToken);
        bytes[0] ^= 1;
        await File.WriteAllBytesAsync(policy, bytes, TestContext.Current!.Execution.CancellationToken);
        var result = await GovernanceNodeProcess.RunAsync(fixture.Root);
        await Assert.That(result.ExitCode).IsEqualTo(FailureExit);
        await Assert.That(result.Error).Contains(PrefixFailureMarker);
    }

    [Test]
    public async Task MissingInventoriedProjectStillFails()
    {
        using var fixture = await GovernanceRootFixture.CreateAsync(TestContext.Current!.Execution.CancellationToken);
        var project = fixture.PathFor(ProjectPath);
        File.Delete(project);
        var result = await GovernanceNodeProcess.RunAsync(fixture.Root);
        await Assert.That(result.ExitCode).IsEqualTo(FailureExit);
        await Assert.That(result.Error).Contains($"{InventoryFailureMarker}{ProjectPath}");
    }
}
