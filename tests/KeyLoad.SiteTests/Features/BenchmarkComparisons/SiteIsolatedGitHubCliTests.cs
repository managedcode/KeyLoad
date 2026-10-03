namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedGitHubCliTests
{
    [Test]
    [Arguments(SiteIsolatedGitHubFields.UnknownCommand)]
    [Arguments(SiteIsolatedGitHubFields.VerifyInputs)]
    [Arguments(SiteIsolatedGitHubFields.FreshCommand)]
    [Arguments(SiteIsolatedGitHubFields.Capture)]
    [Arguments(SiteIsolatedGitHubFields.CaptureMetadata)]
    public async Task AC_ISO_009_ClosedCliRejectsUnknownCommandsAndMissingRequiredArguments(string command)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var result = await SiteIsolatedGitHubScope.RunAsync(SiteIsolatedGitHubFields.CliOperation,
            new[] { command }, token);
        await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsFalse();
    }

    [Test]
    public async Task AC_ISO_009_ClosedCliRejectsUnknownDuplicateAndRelativePathArguments()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var inputs = await SiteIsolatedGitHubInputs.ReadAsync(token);
        var valid = new[]
        {
            SiteIsolatedGitHubFields.VerifyInputs,
            SiteIsolatedGitHubFields.InputArgument + inputs.Capture,
            SiteIsolatedGitHubFields.ReceiptArgument + inputs.Receipt,
        };
        string[][] arguments =
        [
            [.. valid, SiteIsolatedGitHubFields.UnknownArgument],
            [.. valid, valid[SiteIsolatedGitHubTokens.One]],
            [SiteIsolatedGitHubFields.VerifyInputs, SiteIsolatedGitHubFields.RelativeInput, valid[^SiteIsolatedGitHubTokens.One]],
        ];
        foreach (var values in arguments)
        {
            var result = await SiteIsolatedGitHubScope.RunAsync(SiteIsolatedGitHubFields.CliOperation, values, token);
            await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsFalse();
        }
    }
}
