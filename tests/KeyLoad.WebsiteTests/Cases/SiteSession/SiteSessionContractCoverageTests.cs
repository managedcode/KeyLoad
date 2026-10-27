using System.Diagnostics;
using System.Text.Json;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

/// <summary>Controlled parser inputs exercise current production modules and never become measurement evidence.</summary>
internal sealed class SiteSessionContractCoverageTests
{
    private const string Accepted = "accepted";
    private const string Rejections = "rejections";
    private const string ExpectedRejections = "expectedRejections";
    private const string Fixture = "tests/KeyLoad.WebsiteTests/Fixtures/session-contract-probe.mjs";

    [Test]
    [Arguments("json")]
    [Arguments("plan")]
    [Arguments("reports")]
    [Arguments("selection")]
    [Arguments("environment")]
    [Arguments("qualification")]
    [Arguments("runtime")]
    [Arguments("site-context")]
    [Arguments("publication")]
    [Arguments("profile-accounting")]
    public async Task CurrentSessionContractsRejectAlteredIdentityAccountingAndAdmission(string scenario)
    {
        var inputs = SiteTestInputs.Read();
        var start = new ProcessStartInfo(SiteTokens.NodeExecutable)
        {
            WorkingDirectory = inputs.Repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(Path.Combine(inputs.Repository, Fixture));
        start.ArgumentList.Add(scenario);
        var result = await SiteIsolatedNodeProcess.RunProcessAsync(start, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.StandardError);
        await Assert.That(result.StandardError).IsEmpty();
        using var document = JsonDocument.Parse(result.StandardOutput);
        var value = document.RootElement;
        await Assert.That(value.GetProperty(Accepted).GetBoolean()).IsTrue();
        await Assert.That(value.GetProperty(Rejections).GetInt32()).IsGreaterThan(0);
        await Assert.That(value.GetProperty(Rejections).GetInt32())
            .IsEqualTo(value.GetProperty(ExpectedRejections).GetInt32());
    }
}
