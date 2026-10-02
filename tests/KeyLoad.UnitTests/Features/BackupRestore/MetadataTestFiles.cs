using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MetadataTestFiles
{
    internal static async Task PadWithWhitespaceAsync(string path, int targetLength)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        if (file.Length > targetLength)
        {
            throw new InvalidOperationException(MetadataTestContract.FixtureSizeError);
        }

        var remaining = targetLength - (int)file.Length;
        file.Position = file.Length;
        var whitespace = new byte[Math.Min(MetadataTestContract.WhitespaceChunkBytes, remaining)];
        Array.Fill(whitespace, MetadataTestContract.JsonWhitespace);
        while (remaining > 0)
        {
            var count = Math.Min(remaining, whitespace.Length);
            await file.WriteAsync(whitespace.AsMemory(0, count));
            remaining -= count;
        }
    }

    internal static async Task UpdateManifestFileAsync(string backupDirectory, string fileName)
    {
        var path = Path.Combine(backupDirectory, fileName);
        var manifestPath = Path.Combine(backupDirectory, MetadataTestContract.ManifestFileName);
        var manifest = JsonNode.Parse(await File.ReadAllBytesAsync(manifestPath))!.AsObject();
        var files = manifest[MetadataTestContract.FilesJsonKey]!.AsArray();
        var entry = files.Single(candidate =>
            candidate![MetadataTestContract.NameJsonKey]!.GetValue<string>() == fileName)!.AsObject();
        await using var file = File.OpenRead(path);
        entry[MetadataTestContract.LengthJsonKey] = file.Length;
        entry[MetadataTestContract.ChecksumJsonKey] = Convert.ToHexStringLower(await SHA256.HashDataAsync(file));
        await File.WriteAllBytesAsync(manifestPath, JsonDefaults.Serialize(manifest));
    }

    internal static async Task BreakIdentityEnvelopeChecksumAsync(string backupDirectory)
    {
        var path = Path.Combine(backupDirectory, MetadataTestContract.IdentityFileName);
        var envelope = JsonNode.Parse(await File.ReadAllBytesAsync(path))!.AsObject();
        var checksum = Convert.FromBase64String(envelope[MetadataTestContract.ChecksumJsonKey]!.GetValue<string>());
        checksum[0] ^= 1;
        envelope[MetadataTestContract.ChecksumJsonKey] = Convert.ToBase64String(checksum);
        await File.WriteAllBytesAsync(path, JsonDefaults.Serialize(envelope));
        await UpdateManifestFileAsync(backupDirectory, MetadataTestContract.IdentityFileName);
    }
}
