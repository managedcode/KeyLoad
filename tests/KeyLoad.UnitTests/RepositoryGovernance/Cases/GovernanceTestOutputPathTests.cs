namespace KeyLoad.UnitTests.RepositoryGovernance;

/// <summary>AC-MCAF-010: executable test paths stay beside the compiled test assembly.</summary>
internal sealed class GovernanceTestOutputPathTests
{
    private const int SuccessExit = 0;
    private const int FailureExit = 1;
    private const string SourcePath = "tests/KeyLoad.UnitTests/Cases/PathOwnership.cs";
    private const string FailureMarker = "Test-owned files must use the executing assembly output directory: ";
    private const string TemporaryPathCall = "var path = Path.GetTempPath();";
    private const string TemporaryFileCall = "var path = System.IO.Path.GetTempFileName();";
    private const string TemporaryDirectoryCall = "var path = Directory.CreateTempSubdirectory();";
    private const string SystemPathCombine = "var path = Path.Combine(\"/tmp\", \"owned\");";
    private const string SystemDirectoryCreation = "Directory.CreateDirectory(\"/private/var/folders/owned\");";
    private const string SystemFileCreation = "File.WriteAllText(\"/var/tmp/owned\", \"content\");";
    private const string OutputDirectory = "var path = Path.Combine(AppContext.BaseDirectory, Guid.NewGuid().ToString());";
    private const string EvidenceString = "var evidence = \"Path.GetTempPath() /private/var/folders/owned\";";
    private const string CommentEvidence = "// Path.GetTempFileName()\n/* Directory.CreateTempSubdirectory() */";
    private const string RawStringEvidence = "var evidence = \"\"\"Path.GetTempPath()\"\"\";";

    [Test]
    [Arguments(TemporaryPathCall)]
    [Arguments(TemporaryFileCall)]
    [Arguments(TemporaryDirectoryCall)]
    [Arguments(SystemPathCombine)]
    [Arguments(SystemDirectoryCreation)]
    [Arguments(SystemFileCreation)]
    public async Task ExecutableSystemTemporaryPathsFail(string source)
    {
        using var fixture = await GovernanceRootFixture.CreateAsync(TestContext.Current!.Execution.CancellationToken);
        await WriteSourceAsync(fixture, source);
        var result = await GovernanceNodeProcess.RunAsync(fixture.Root);
        await Assert.That(result.ExitCode).IsEqualTo(FailureExit);
        await Assert.That(result.Error).Contains($"{FailureMarker}{SourcePath}:");
    }

    [Test]
    [Arguments(OutputDirectory)]
    [Arguments(EvidenceString)]
    [Arguments(CommentEvidence)]
    [Arguments(RawStringEvidence)]
    public async Task AssemblyOutputAndInertEvidencePass(string source)
    {
        using var fixture = await GovernanceRootFixture.CreateAsync(TestContext.Current!.Execution.CancellationToken);
        await WriteSourceAsync(fixture, source);
        var result = await GovernanceNodeProcess.RunAsync(fixture.Root);
        await Assert.That(result.ExitCode).IsEqualTo(SuccessExit).Because(result.Error);
        await Assert.That(result.Error).IsEmpty();
    }

    private static async Task WriteSourceAsync(GovernanceRootFixture fixture, string source)
    {
        var path = fixture.PathFor(SourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, source, TestContext.Current!.Execution.CancellationToken);
    }
}
