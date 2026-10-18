using System.IO.Compression;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedGitHubProviderRejectionTests
{
    [Test]
    public async Task AC_ISO_007_ProviderInventoryRejectsAnUnknownCellBeforeOwnedOutput()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var inputs = await SiteIsolatedGitHubInputs.ReadAsync(token);
        await using var temporary = SiteTempDirectory.Create();
        var path = Path.Combine(temporary.Path, SiteIsolatedGitHubFields.ControlledArchive);
        await WriteCorruptProviderAsync(inputs, path, token);
        await using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            SiteIsolatedGitHubArchivePreflight.InspectAsync(archive,
                SiteIsolatedGitHubArchivePaths.ExpectedProvider(), provider: true, token,
                SiteIsolatedGitHubArchivePaths.CellIds(inputs.Metadata)));
        await Assert.That(error!.Message).IsEqualTo(SiteIsolatedGitHubTokens.InvalidPath);
        await Assert.That(Directory.Exists(temporary.Output)).IsFalse();
    }

    private static async Task WriteCorruptProviderAsync(SiteIsolatedGitHubInputs inputs,
        string path, CancellationToken token)
    {
        await using var input = File.OpenRead(Path.Combine(inputs.Capture,
            SiteIsolatedGitHubTokens.Archives, SiteIsolatedGitHubTokens.Provider));
        using var source = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var destination = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        await using var original = await source.GetEntry(SiteIsolatedGitHubTokens.Proof)!.OpenAsync(token);
        await using var corrupt = await destination.CreateEntry(SiteIsolatedGitHubFields.UnknownCellInventory,
            CompressionLevel.NoCompression).OpenAsync(token);
        await original.CopyToAsync(corrupt, token);
    }
}
