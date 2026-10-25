using System.IO.Compression;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

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
        var path = (ExtraPath(mutation)).Value;
        if (path is not null)
        {
            var extra = archive.CreateEntry(path, CompressionLevel.NoCompression);
            await CopyAsync(original, extra, token);
        }
    }

    private static (bool HasValue, string? Value) ExtraPath(SiteIsolatedGitHubArchiveMutation mutation) => mutation switch
    {
        SiteIsolatedGitHubArchiveMutation.Traversal => (true, ((string?)(SiteIsolatedGitHubFields.Traversal))),
        SiteIsolatedGitHubArchiveMutation.Unexpected => (true, ((string?)(SiteIsolatedGitHubFields.Unexpected))),
        SiteIsolatedGitHubArchiveMutation.Duplicate => (true, ((string?)(SiteIsolatedGitHubTokens.Manifest))),
        SiteIsolatedGitHubArchiveMutation.CaseDuplicate => (true, ((string?)(SiteIsolatedGitHubFields.CaseDuplicate))),
        _ => (false, default(string?))
    };

    private static async Task CopyAsync(ZipArchiveEntry original, ZipArchiveEntry target, CancellationToken token)
    {
        await using var input = await original.OpenAsync(token);
        await using var output = await target.OpenAsync(token);
        await input.CopyToAsync(output, token);
    }
}
