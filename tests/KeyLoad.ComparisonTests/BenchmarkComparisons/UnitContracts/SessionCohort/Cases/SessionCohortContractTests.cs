using System.Text.Json;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-BSESSION-001/007: parser fixtures never become published measurements.</summary>
internal sealed class SessionCohortContractTests
{
    private const string Plan = "plan";
    private const string Reports = "reports";
    private const string Selection = "selection";
    private const string Accepted = "accepted";
    private const string Rejections = "rejections";
    private const string ExpectedRejections = "expectedRejections";

    [Test]
    [Arguments(Plan)]
    [Arguments(Reports)]
    [Arguments(Selection)]
    [Arguments("environment")]
    [Arguments("qualification")]
    [Arguments("runtime")]
    [Arguments("site-context")]
    [Arguments("publication")]
    [Arguments("profile-accounting")]
    public async Task ActualSessionModulesAcceptCompleteInputAndRejectChangedIdentityOrAccounting(string scenario)
    {
        var result = await SessionCohortNodeProcess.RunAsync(scenario, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        await Assert.That(result.Error).IsEmpty();
        using var document = JsonDocument.Parse(result.Output);
        var value = document.RootElement;
        await Assert.That(value.GetProperty(Accepted).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(Rejections).GetInt32())
            .IsEqualTo(value.GetProperty(ExpectedRejections).GetInt32());
    }
}
