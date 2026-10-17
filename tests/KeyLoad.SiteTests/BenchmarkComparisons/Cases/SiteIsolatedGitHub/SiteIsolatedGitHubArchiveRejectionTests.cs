using System.IO.Compression;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedGitHubArchiveRejectionTests
{
    [Test]
    [Arguments(SiteIsolatedGitHubArchiveMutation.Traversal, SiteIsolatedGitHubTokens.InvalidPath)]
    [Arguments(SiteIsolatedGitHubArchiveMutation.Unexpected, SiteIsolatedGitHubTokens.InvalidPath)]
    [Arguments(SiteIsolatedGitHubArchiveMutation.Duplicate, SiteIsolatedGitHubTokens.Duplicate)]
    [Arguments(SiteIsolatedGitHubArchiveMutation.CaseDuplicate, SiteIsolatedGitHubTokens.Duplicate)]
    [Arguments(SiteIsolatedGitHubArchiveMutation.Symlink, SiteIsolatedGitHubTokens.InvalidPath)]
    [Arguments(SiteIsolatedGitHubArchiveMutation.Special, SiteIsolatedGitHubTokens.InvalidPath)]
    [Arguments(SiteIsolatedGitHubArchiveMutation.Missing, SiteIsolatedGitHubTokens.Incomplete)]
    public async Task AC_ISO_007_CompleteBclPreflightRejectsCorruptArchiveBeforeOwnedOutput(
        SiteIsolatedGitHubArchiveMutation mutation, string expected)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var inputs = await SiteIsolatedGitHubInputs.ReadAsync(token);
        await using var temporary = SiteTempDirectory.Create();
        var corrupt = Path.Combine(temporary.Path, SiteIsolatedGitHubFields.ControlledArchive);
        await SiteIsolatedGitHubArchiveMutationWriter.CreateAsync(inputs, corrupt, mutation, token);
        await using var stream = File.OpenRead(corrupt);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            SiteIsolatedGitHubArchivePreflight.InspectAsync(archive,
                SiteIsolatedGitHubArchivePaths.ExpectedSuite(inputs.Metadata), provider: false, token));
        await Assert.That(error!.Message).IsEqualTo(expected);
        await Assert.That(Directory.Exists(temporary.Output)).IsFalse();
    }
}

internal enum SiteIsolatedGitHubArchiveMutation
{
    Traversal,
    Unexpected,
    Duplicate,
    CaseDuplicate,
    Symlink,
    Special,
    Missing,
}

internal static class SiteIsolatedGitHubArchiveMutationWriter
{
    private const int SymbolicLinkAttributes = 0xA000 << SiteIsolatedGitHubTokens.UnixShift;
    private const int SpecialAttributes = 0x6000 << SiteIsolatedGitHubTokens.UnixShift;

    public static async Task CreateAsync(SiteIsolatedGitHubInputs inputs, string output,
        SiteIsolatedGitHubArchiveMutation mutation, CancellationToken token)
    {
        await using var sourceStream = File.OpenRead(Path.Combine(inputs.Capture,
            SiteIsolatedGitHubTokens.Archives, SiteIsolatedGitHubTokens.Suite));
        using var source = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: true);
        await using var destination = new FileStream(output, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        var original = source.GetEntry(SiteIsolatedGitHubTokens.Manifest)!;
        var first = archive.CreateEntry(SiteIsolatedGitHubTokens.Manifest, CompressionLevel.NoCompression);
        if (mutation == SiteIsolatedGitHubArchiveMutation.Symlink)
        {
            first.ExternalAttributes = SymbolicLinkAttributes;
        }
        else if (mutation == SiteIsolatedGitHubArchiveMutation.Special)
        {
            first.ExternalAttributes = SpecialAttributes;
        }

        await CopyAsync(original, first, token);
        var path = ExtraPath(mutation);
        if (path is not null)
        {
            var extra = archive.CreateEntry(path, CompressionLevel.NoCompression);
            await CopyAsync(original, extra, token);
        }
    }

    private static string? ExtraPath(SiteIsolatedGitHubArchiveMutation mutation) => mutation switch
    {
        SiteIsolatedGitHubArchiveMutation.Traversal => SiteIsolatedGitHubFields.Traversal,
        SiteIsolatedGitHubArchiveMutation.Unexpected => SiteIsolatedGitHubFields.Unexpected,
        SiteIsolatedGitHubArchiveMutation.Duplicate => SiteIsolatedGitHubTokens.Manifest,
        SiteIsolatedGitHubArchiveMutation.CaseDuplicate => SiteIsolatedGitHubFields.CaseDuplicate,
        _ => null,
    };

    private static async Task CopyAsync(ZipArchiveEntry original, ZipArchiveEntry target, CancellationToken token)
    {
        await using var input = await original.OpenAsync(token);
        await using var output = await target.OpenAsync(token);
        await input.CopyToAsync(output, token);
    }
}
