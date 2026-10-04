using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedGitHubInputCorruption
{
    public static async Task VerifyAsync(SiteIsolatedGitHubArchiveReceipt receipt, CancellationToken token)
    {
        var names = new[]
        {
            Relative(SiteIsolatedGitHubTokens.Aggregate, SiteIsolatedGitHubTokens.Manifest),
            Relative(SiteIsolatedGitHubTokens.ProviderDirectory, SiteIsolatedGitHubTokens.Proof),
            Relative(SiteIsolatedGitHubTokens.ProviderDirectory, SiteIsolatedGitHubTokens.ImageProof),
            Relative(SiteIsolatedGitHubTokens.ProviderDirectory, SiteIsolatedGitHubTokens.Images +
                SiteIsolatedGitHubTokens.Slash + SiteIsolatedGitHubTokens.ServerManifest),
            receipt.Files.First(file => file.Path.EndsWith(SiteIsolatedGitHubTokens.Slash +
                SiteIsolatedGitHubTokens.Raw, StringComparison.Ordinal)).Path,
        };
        foreach (var name in names)
        {
            await VerifyRewrittenInputRejectedAsync(receipt, name, token);
        }

        await SiteIsolatedGitHubArchiveSetup.VerifyUnchangedAsync(receipt, token);
    }

    private static async Task VerifyRewrittenInputRejectedAsync(SiteIsolatedGitHubArchiveReceipt receipt,
        string relative, CancellationToken token)
    {
        var path = SiteIsolatedGitHubFileOperations.FixedTarget(receipt.Capture, relative);
        await DetachAsync(path, token);
        var original = await File.ReadAllBytesAsync(path, token);
        var originalReceipt = await File.ReadAllBytesAsync(receipt.ReceiptPath, token);
        var altered = JsonNode.Parse(original)!.AsObject();
        altered[SiteIsolatedGitHubTokens.Extra] = true;
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(altered);
        var changed = receipt.Value.DeepClone().AsObject();
        var file = changed[SiteIsolatedGitHubTokens.InputFiles]!.AsArray().Single(item =>
            item![SiteIsolatedGitHubTokens.Path]!.GetValue<string>() == relative)!;
        file[SiteIsolatedGitHubTokens.Bytes] = bytes.LongLength;
        file[SiteIsolatedGitHubTokens.Sha256] = Convert.ToHexStringLower(SHA256.HashData(bytes));
        try
        {
            await File.WriteAllBytesAsync(path, bytes, token);
            await File.WriteAllTextAsync(receipt.ReceiptPath, changed.ToJsonString(), token);
            var result = await SiteIsolatedGitHubScope.RunAsync(SiteIsolatedGitHubFields.InputsOperation,
                new { input = receipt.Capture, receipt = receipt.ReceiptPath }, token);
            await Assert.That(result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean()).IsFalse();
            var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                SiteIsolatedGitHubArchiveSetup.VerifyUnchangedAsync(receipt, token));
            await Assert.That(error!.Message).IsEqualTo(SiteIsolatedGitHubTokens.Changed);
        }
        finally
        {
            await File.WriteAllBytesAsync(path, original, CancellationToken.None);
            await File.WriteAllBytesAsync(receipt.ReceiptPath, originalReceipt, CancellationToken.None);
        }
    }

    private static async Task DetachAsync(string path, CancellationToken token)
    {
        var result = await SiteIsolatedGitHubScope.RunAsync(SiteIsolatedGitHubFields.DetachOperation,
            new { path }, token);
        if (!result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean())
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Changed);
        }
    }

    private static string Relative(string root, string file) => string.Join(SiteIsolatedGitHubTokens.Slash,
        SiteIsolatedGitHubTokens.Input, root, file);
}
