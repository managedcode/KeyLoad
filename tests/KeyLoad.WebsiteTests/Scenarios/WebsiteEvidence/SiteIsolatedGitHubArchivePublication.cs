using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal static class SiteIsolatedGitHubArchivePublication
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static async Task<SiteIsolatedGitHubArchiveReceipt> PublishAsync(string capture, string receiptPath,
        JsonObject metadata, SiteIsolatedGitHubArchiveFile[] files, CancellationToken token)
    {
        var input = Path.Combine(capture, SiteIsolatedGitHubTokens.Input);
        var stage = Path.Combine(capture, SiteIsolatedGitHubArchiveReader.PreparedInput);
        var pending = receiptPath + Guid.NewGuid().ToString(SiteIsolatedGitHubFields.GuidFormat);
        var inputOwned = false;
        var receiptOwned = false;
        var pendingOwned = false;
        try
        {
            await SiteIsolatedGitHubArchiveReader.VerifyPreparedAsync(capture, files, token);
            await SiteIsolatedGitHubArchiveReader.VerifyArchiveHashesAsync(capture, metadata, token);
            var value = metadata.DeepClone().AsObject();
            value[SiteIsolatedGitHubTokens.State] = SiteIsolatedGitHubTokens.ArchiveState;
            await WritePendingAsync(pending, value, token);
            pendingOwned = true;
            Directory.Move(stage, input);
            inputOwned = true;
            File.Move(pending, receiptPath, overwrite: false);
            receiptOwned = true;
            var receipt = new SiteIsolatedGitHubArchiveReceipt(capture, receiptPath, value, files);
            await SiteIsolatedGitHubArchiveSetup.VerifyInputsAsync(receipt, captureCoverage: false, token);
            return receipt;
        }
        catch (Exception)
        {
            if (inputOwned)
            {
                Directory.Delete(input, recursive: true);
            }

            if (receiptOwned)
            {
                File.Delete(receiptPath);
            }

            throw;
        }
        finally
        {
            if (pendingOwned && File.Exists(pending))
            {
                File.Delete(pending);
            }
        }
    }

    private static async Task WritePendingAsync(string path, JsonObject value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        if (bytes.LongLength > SiteIsolatedGitHubTokens.JsonBytes)
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Bounds);
        }

        var owned = false;
        try
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            owned = true;
            await output.WriteAsync(bytes, token);
            await output.FlushAsync(token);
        }
        catch (Exception)
        {
            if (owned)
            {
                File.Delete(path);
            }

            throw;
        }
    }
}
