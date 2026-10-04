namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Exercises the real comparison CLI's configuration boundary in a child process.</summary>
internal sealed class ComparisonHostStartupTests
{
    private const string MissingKeyLoadSetting = "Missing benchmark setting: Benchmarks:KeyLoadEndpoint";
    private const string MissingQdrantSetting = "Missing benchmark setting: Benchmarks:QdrantEndpoint";
    private const string InvalidBudgetDetail = "The benchmark configuration exceeds its budgets.";
    private const string InvalidDimensionsArgument = "--Benchmarks:Dimensions=1";
    private const string ValidDimensionsArgument = "--Benchmarks:Dimensions=2";
    private const string DimensionsEnvironmentKey = "Benchmarks__Dimensions";
    private const string KeyLoadEndpointEnvironmentKey = "Benchmarks__KeyLoadEndpoint";
    private const string ValidEndpoint = "http://127.0.0.1:1";
    private const string InvalidDimensions = "1";
    private const int SuccessExitCode = 0;

    [Test]
    public async Task AcHost003MissingSettingsReportFirstRequiredEndpoint()
    {
        var result = await ComparisonHostProcess.RunAsync([], cancellationToken: TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result.ExitCode).IsNotEqualTo(SuccessExitCode);
        await Assert.That(result.Stderr.Contains(MissingKeyLoadSetting, StringComparison.Ordinal)).IsTrue();
        await Assert.That(result.Stderr.Contains(MissingQdrantSetting, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task AcHost003InvalidDimensionsFailBeforeRequiredSettingsOrClients()
    {
        var result = await ComparisonHostProcess.RunAsync([InvalidDimensionsArgument],
            cancellationToken: TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result.ExitCode).IsNotEqualTo(SuccessExitCode);
        await Assert.That(result.Stderr.Contains(InvalidBudgetDetail, StringComparison.Ordinal)).IsTrue();
        await Assert.That(result.Stderr.Contains(MissingKeyLoadSetting, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task AcHost003CommandLineOverridesInvalidEnvironmentDimension()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [DimensionsEnvironmentKey] = InvalidDimensions
        };
        var result = await ComparisonHostProcess.RunAsync([ValidDimensionsArgument], environment,
            TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result.ExitCode).IsNotEqualTo(SuccessExitCode);
        await Assert.That(result.Stderr.Contains(MissingKeyLoadSetting, StringComparison.Ordinal)).IsTrue();
        await Assert.That(result.Stderr.Contains(InvalidBudgetDetail, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task AcHost003FirstEndpointAdvancesToSecondRequiredEndpoint()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [KeyLoadEndpointEnvironmentKey] = ValidEndpoint
        };
        var result = await ComparisonHostProcess.RunAsync([], environment,
            TestContext.Current!.Execution.CancellationToken);

        await Assert.That(result.ExitCode).IsNotEqualTo(SuccessExitCode);
        await Assert.That(result.Stderr.Contains(MissingQdrantSetting, StringComparison.Ordinal)).IsTrue();
        await Assert.That(result.Stderr.Contains(MissingKeyLoadSetting, StringComparison.Ordinal)).IsFalse();
    }
}
