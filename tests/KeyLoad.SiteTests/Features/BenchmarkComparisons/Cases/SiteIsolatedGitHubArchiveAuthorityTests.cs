using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedGitHubArchiveAuthorityTests
{
    private const byte LeadingSpace = 32;

    [Test]
    public async Task AC_ISO_007_OriginalArchiveAuthoritySurvivesChangesToCallerCopies()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var inputs = await SiteIsolatedGitHubInputs.ReadAsync(token);
        var bytes = await File.ReadAllBytesAsync(inputs.Receipt, token);
        var value = JsonNode.Parse(bytes)!.AsObject();
        var original = value.DeepClone();
        var files = value[SiteIsolatedGitHubTokens.InputFiles]!.AsArray().Select(file =>
            new SiteIsolatedGitHubArchiveFile(file![SiteIsolatedGitHubTokens.Path]!.GetValue<string>(),
                file[SiteIsolatedGitHubTokens.Bytes]!.GetValue<long>(),
                file[SiteIsolatedGitHubTokens.Sha256]!.GetValue<string>())).ToArray();
        var expectedPath = files[SiteIsolatedGitHubTokens.Zero].Path;
        var receipt = new SiteIsolatedGitHubArchiveReceipt(inputs.Capture, inputs.Receipt, value, files);
        value[SiteIsolatedGitHubTokens.Extra] = true;
        files[SiteIsolatedGitHubTokens.Zero] = files[SiteIsolatedGitHubTokens.Zero] with
        {
            Path = SiteIsolatedGitHubFields.Unexpected,
        };
        var exposed = receipt.Value;
        exposed[SiteIsolatedGitHubTokens.Extra] = true;
        var exposedFiles = receipt.Files;
        exposedFiles[SiteIsolatedGitHubTokens.Zero] = exposedFiles[SiteIsolatedGitHubTokens.Zero] with
        {
            Path = SiteIsolatedGitHubFields.Unexpected,
        };
        await Assert.That(receipt.Matches(original)).IsTrue();
        await Assert.That(receipt.MatchesBytes(bytes)).IsTrue();
        var rewrittenBytes = new byte[bytes.Length + 1];
        rewrittenBytes[SiteIsolatedGitHubTokens.Zero] = LeadingSpace;
        bytes.CopyTo(rewrittenBytes, 1);
        await Assert.That(receipt.MatchesBytes(rewrittenBytes)).IsFalse();
        await Assert.That(receipt.Files[SiteIsolatedGitHubTokens.Zero].Path)
            .IsEqualTo(expectedPath);
        await SiteIsolatedGitHubArchiveSetup.VerifyUnchangedAsync(receipt, token);
    }
}
