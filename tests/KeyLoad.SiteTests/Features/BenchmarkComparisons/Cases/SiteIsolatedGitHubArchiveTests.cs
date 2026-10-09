using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedGitHubArchiveTests
{
    private const int ProviderInputFiles = 60;
    private const int SuiteInputFiles = 1656;

    [Test]
    public async Task AC_ISO_007_CurrentArchivesContainExact1716SelectedFilesWithOriginalHashes()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var inputs = await SiteIsolatedGitHubInputs.ReadAsync(token);
        var receipt = JsonNode.Parse(await File.ReadAllBytesAsync(inputs.Receipt, token))!.AsObject();
        await AssertArchiveAsync(inputs, receipt, SiteIsolatedGitHubTokens.Suite, false, token);
        await AssertArchiveAsync(inputs, receipt, SiteIsolatedGitHubTokens.Provider, true, token);
        await Assert.That(inputs.Metadata[SiteIsolatedGitHubTokens.Workers]!.AsArray().Count)
            .IsEqualTo(SiteIsolatedGitHubTokens.WorkerCount);
        var files = receipt[SiteIsolatedGitHubTokens.InputFiles]!.AsArray();
        await Assert.That(files.Count).IsEqualTo(SiteIsolatedGitHubTokens.FileCount);
        await Assert.That(files.Count(file => file![SiteIsolatedGitHubTokens.Path]!.GetValue<string>()
            .StartsWith("input/provider/", StringComparison.Ordinal))).IsEqualTo(ProviderInputFiles);
        await Assert.That(files.Count(file => file![SiteIsolatedGitHubTokens.Path]!.GetValue<string>()
            .StartsWith("input/aggregate/", StringComparison.Ordinal))).IsEqualTo(SuiteInputFiles);
    }

    [Test]
    public async Task AC_ISO_007_RejectsActualArchiveReusedOverAnExistingPreparedInput()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var inputs = await SiteIsolatedGitHubInputs.ReadAsync(token);
        var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            SiteIsolatedGitHubArchiveSetup.PrepareAsync(inputs.Capture, inputs.Receipt, token));
        await Assert.That(error!.Message).IsEqualTo(SiteIsolatedGitHubTokens.Exists);
    }

    private static async Task AssertArchiveAsync(SiteIsolatedGitHubInputs inputs, JsonObject receipt,
        string name, bool provider, CancellationToken token)
    {
        await using var stream = File.OpenRead(Path.Combine(inputs.Capture, SiteIsolatedGitHubTokens.Archives, name));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var expected = provider ? SiteIsolatedGitHubArchivePaths.ExpectedProvider() :
            SiteIsolatedGitHubArchivePaths.ExpectedSuite(inputs.Metadata);
        var selected = await SiteIsolatedGitHubArchivePreflight.InspectAsync(archive, expected, provider, token,
            SiteIsolatedGitHubArchivePaths.CellIds(inputs.Metadata));
        await Assert.That(selected.Length).IsEqualTo(expected.Count);
        var prefix = string.Join(SiteIsolatedGitHubTokens.Slash, SiteIsolatedGitHubTokens.Input,
            provider ? SiteIsolatedGitHubTokens.ProviderDirectory : SiteIsolatedGitHubTokens.Aggregate) + SiteIsolatedGitHubTokens.Slash;
        foreach (var entry in selected)
        {
            await using var content = await entry.Entry.OpenAsync(token);
            var independentHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(content, token));
            var file = receipt[SiteIsolatedGitHubTokens.InputFiles]!.AsArray().Single(value =>
                value![SiteIsolatedGitHubTokens.Path]!.GetValue<string>() == prefix + entry.File.Path)!;
            await Assert.That(file[SiteIsolatedGitHubTokens.Sha256]!.GetValue<string>()).IsEqualTo(independentHash);
            await Assert.That(file[SiteIsolatedGitHubTokens.Bytes]!.GetValue<long>()).IsEqualTo(entry.Entry.Length);
        }
    }
}
