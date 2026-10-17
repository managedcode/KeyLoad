using System.Globalization;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

[NotInParallel(SiteIsolatedGitHubFields.NativeFixtureKey)]
internal sealed class SiteIsolatedGitHubNativeCaptureTests
{
    [Test]
    public async Task AC_ISO_009_ActualPagesProviderCapturesTwoImmutableArchivesMatchingPreparedAuthority()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var inputs = await SiteIsolatedGitHubInputs.ReadAsync(token);
        var site = SiteTestInputs.Read();
        await using var temporary = SiteTempDirectory.Create();
        var capture = Path.Combine(temporary.Path, SiteIsolatedGitHubFields.Capture);
        SiteIsolatedGitHubNativeArchiveAssertions.RequireAvailableDisk(inputs.Metadata, temporary.Path);
        var start = SiteIsolatedGitHubNodeProcess.CreateStart(site.Repository);
        start.ArgumentList.Add(Path.Combine(site.Repository, SiteIsolatedGitHubTokens.Module));
        start.ArgumentList.Add(SiteIsolatedGitHubFields.Capture);
        AddArguments(start, inputs, capture);
        var result = await SiteIsolatedGitHubNativeProcess.RunAsync(start, token);
        await Assert.That(result.ExitCode).IsEqualTo(SiteIsolatedGitHubTokens.Zero);
        await Assert.That(result.StandardError.Length).IsEqualTo(SiteIsolatedGitHubTokens.Zero);
        using var native = JsonDocument.Parse(result.StandardOutput);
        await Assert.That(native.RootElement.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsTrue();
        await SiteIsolatedGitHubNativeArchiveAssertions.VerifyAsync(inputs.Metadata, capture, token);
        await Assert.That(Directory.Exists(Path.Combine(capture, SiteIsolatedGitHubTokens.Input))).IsFalse();
        await VerifyMetadataCaptureAsync(site.Repository, inputs, temporary.Path, token);
    }

    private static async Task VerifyMetadataCaptureAsync(string repository, SiteIsolatedGitHubInputs inputs,
        string temporary, CancellationToken token)
    {
        var capture = Path.Combine(temporary, SiteIsolatedGitHubFields.MetadataCapture);
        var start = SiteIsolatedGitHubNodeProcess.CreateStart(repository);
        start.ArgumentList.Add(Path.Combine(repository, SiteIsolatedGitHubTokens.Module));
        start.ArgumentList.Add(SiteIsolatedGitHubFields.CaptureMetadata);
        AddArguments(start, inputs, capture);
        var result = await SiteIsolatedGitHubNativeProcess.RunAsync(start, token);
        await Assert.That(result.ExitCode).IsEqualTo(SiteIsolatedGitHubTokens.Zero);
        await Assert.That(result.StandardError.Length).IsEqualTo(SiteIsolatedGitHubTokens.Zero);
        await Assert.That(File.Exists(Path.Combine(capture, SiteIsolatedGitHubTokens.Metadata))).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(capture, SiteIsolatedGitHubTokens.Archives))).IsFalse();
        await SiteIsolatedGitHubNativeArchiveAssertions.VerifyMetadataAsync(inputs.Metadata, capture, token);
    }

    private static void AddArguments(System.Diagnostics.ProcessStartInfo start,
        SiteIsolatedGitHubInputs inputs, string capture)
    {
        start.ArgumentList.Add(SiteIsolatedGitHubFields.InputArgument + capture);
        start.ArgumentList.Add(SiteIsolatedGitHubFields.ModeArgument + SiteIsolatedGitHubTokens.Validate);
        start.ArgumentList.Add(SiteIsolatedGitHubFields.SiteArgument + inputs.Metadata[SiteIsolatedGitHubTokens.Source]!
            [SiteIsolatedGitHubTokens.Website]!.GetValue<string>());
        start.ArgumentList.Add(SiteIsolatedGitHubFields.ControlArgument + inputs.Metadata[SiteIsolatedGitHubTokens.Source]!
            [SiteIsolatedGitHubTokens.Control]!.GetValue<string>());
        start.ArgumentList.Add(SiteIsolatedGitHubFields.RunArgument + inputs.Metadata[SiteIsolatedGitHubTokens.Run]!
            [SiteIsolatedGitHubTokens.Id]!.GetValue<long>().ToString(CultureInfo.InvariantCulture));
    }
}
