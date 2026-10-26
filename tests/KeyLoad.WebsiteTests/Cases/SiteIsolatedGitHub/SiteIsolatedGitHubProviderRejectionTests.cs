namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal sealed class SiteIsolatedGitHubProviderRejectionTests
{
    [Test]
    [Arguments("extra", SiteIsolatedGitHubTokens.Incomplete)]
    [Arguments("missing", SiteIsolatedGitHubTokens.Incomplete)]
    [Arguments("link", SiteIsolatedGitHubTokens.InvalidPath)]
    public async Task AC_ISO_007_PreparedProviderRejectsChangedInventoryBeforeOwnedOutput(string corruption, string expected)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var inputs = await SiteIsolatedGitHubInputs.ReadAsync(token);
        await using var temporary = SiteTempDirectory.Create();
        var capture = Path.Combine(temporary.Path, SiteIsolatedGitHubFields.Capture);
        var clone = await SiteIsolatedGitHubScope.RunAsync("clone", new { input = inputs.Capture, output = capture }, token);
        await Assert.That(clone.GetProperty(SiteIsolatedFields.Ok).GetBoolean()).IsTrue();
        var input = Path.Combine(capture, SiteIsolatedGitHubTokens.Input);
        var prepared = Path.Combine(capture, SiteIsolatedGitHubArchiveReader.PreparedInput);
        Directory.Move(input, prepared);
        var relative = Path.Combine(SiteIsolatedGitHubTokens.ProviderDirectory, SiteIsolatedGitHubTokens.Proof);
        var target = Path.Combine(prepared, relative);
        if (corruption == "extra")
        {
            await File.WriteAllTextAsync(target + ".unexpected", "unexpected", token);
        }
        else
        {
            File.Delete(target);
            if (corruption == "link")
            {
                File.CreateSymbolicLink(target, Path.Combine(inputs.Capture, SiteIsolatedGitHubTokens.Input, relative));
            }
        }

        var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            SiteIsolatedGitHubArchiveReader.VerifyPreparedAsync(capture, SiteIsolatedGitHubArchiveReader.ReadFiles(inputs.Metadata), token));
        await Assert.That(error!.Message).IsEqualTo(expected);
        await Assert.That(Directory.Exists(input)).IsFalse();
        await SiteIsolatedGitHubArchiveReader.VerifyArchiveHashesAsync(inputs.Capture, inputs.Metadata, token);
    }
}
