using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal sealed class SiteIsolatedGitHubArchiveTests
{
    [Test]
    public async Task AC_ISO_007_CurrentSessionArchivesContainEverySelectedOriginalFileWithOriginalHashes()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var inputs = await SiteIsolatedGitHubInputs.ReadAsync(token);
        var receipt = JsonNode.Parse(await File.ReadAllBytesAsync(inputs.Receipt, token))!.AsObject();
        var workers = inputs.Metadata[SiteIsolatedGitHubTokens.Workers]!.AsArray();
        await Assert.That(workers.Count > 0 && workers.Count <= SiteIsolatedInventory.Workers()).IsTrue();
        foreach (var archive in inputs.Metadata[SiteIsolatedGitHubTokens.WorkerArchives]!.AsArray())
        {
            var id = archive![SiteIsolatedGitHubTokens.Id]!.GetValue<string>();
            var prefix = "input/aggregate/workers/" + id + "/";
            await AssertArchiveAsync(inputs, receipt, archive[SiteIsolatedGitHubTokens.Path]!.GetValue<string>(), prefix, token);
        }

        var runtime = inputs.Metadata[SiteIsolatedFields.RuntimeIdentity]![SiteIsolatedFields.Archive]!;
        await AssertArchiveAsync(inputs, receipt, runtime[SiteIsolatedGitHubTokens.Path]!.GetValue<string>(),
            "input/provider/github/runtime/", token);
        await Assert.That(receipt[SiteIsolatedGitHubTokens.InputFiles]!.AsArray().Count)
            .IsEqualTo(inputs.Metadata[SiteIsolatedGitHubTokens.InputFiles]!.AsArray().Count);
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
        string relative, string prefix, CancellationToken token)
    {
        await using var stream = File.OpenRead(Path.Combine(inputs.Capture, relative));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var selected = receipt[SiteIsolatedGitHubTokens.InputFiles]!.AsArray().Where(file =>
            file![SiteIsolatedGitHubTokens.Path]!.GetValue<string>().StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        await Assert.That(selected.Length > 0).IsTrue();
        foreach (var file in selected)
        {
            var path = file![SiteIsolatedGitHubTokens.Path]!.GetValue<string>();
            var entry = archive.GetEntry(path[prefix.Length..]);
            await Assert.That(entry is not null).IsTrue();
            await using var content = await entry!.OpenAsync(token);
            var independentHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(content, token));
            await Assert.That(file[SiteIsolatedGitHubTokens.Sha256]!.GetValue<string>()).IsEqualTo(independentHash);
            await Assert.That(file[SiteIsolatedGitHubTokens.Bytes]!.GetValue<long>()).IsEqualTo(entry.Length);
        }
    }
}
