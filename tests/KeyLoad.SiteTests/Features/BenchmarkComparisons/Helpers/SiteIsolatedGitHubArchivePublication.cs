using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedGitHubArchivePublication
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static async Task<SiteIsolatedGitHubArchiveReceipt> PublishAsync(string capture, string receiptPath,
        JsonObject metadata, SiteIsolatedGitHubArchiveEntry[] suite, SiteIsolatedGitHubArchiveEntry[] provider,
        SiteIsolatedGitHubArchiveFile suiteDigest, SiteIsolatedGitHubArchiveFile providerDigest, CancellationToken token)
    {
        var input = Path.Combine(capture, SiteIsolatedGitHubTokens.Input);
        var stage = Path.Combine(capture, SiteIsolatedGitHubFields.StagePrefix + Guid.NewGuid().ToString(SiteIsolatedGitHubFields.GuidFormat));
        var pending = receiptPath + Guid.NewGuid().ToString(SiteIsolatedGitHubFields.GuidFormat);
        var inputOwned = false;
        var receiptOwned = false;
        var pendingOwned = false;
        Directory.CreateDirectory(stage);
        try
        {
            var files = await ExtractAsync(stage, suite, provider, token);
            await RequireArchivesUnchangedAsync(capture, metadata, suiteDigest, providerDigest, token);
            var value = CreateReceipt(metadata, suiteDigest, providerDigest, files);
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
            DeleteOwned(stage, input, receiptPath, inputOwned, receiptOwned);
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

    private static void DeleteOwned(string stage, string input, string receipt, bool inputOwned, bool receiptOwned)
    {
        if (Directory.Exists(stage))
        {
            Directory.Delete(stage, recursive: true);
        }

        if (inputOwned)
        {
            Directory.Delete(input, recursive: true);
        }

        if (receiptOwned)
        {
            File.Delete(receipt);
        }
    }

    private static async Task<SiteIsolatedGitHubArchiveFile[]> ExtractAsync(string stage,
        SiteIsolatedGitHubArchiveEntry[] suite, SiteIsolatedGitHubArchiveEntry[] provider, CancellationToken token)
    {
        var files = new List<SiteIsolatedGitHubArchiveFile>(SiteIsolatedGitHubTokens.FileCount);
        foreach (var entry in suite)
        {
            files.Add(await SiteIsolatedGitHubEntryOperations.ExtractAsync(entry, stage,
                SiteIsolatedGitHubTokens.Aggregate, token));
        }

        foreach (var entry in provider)
        {
            files.Add(await SiteIsolatedGitHubEntryOperations.ExtractAsync(entry, stage,
                SiteIsolatedGitHubTokens.ProviderDirectory, token));
        }

        return [.. files.Select(file => file with { Path = SiteIsolatedGitHubTokens.Input + SiteIsolatedGitHubTokens.Slash + file.Path })];
    }

    private static JsonObject CreateReceipt(JsonObject metadata, SiteIsolatedGitHubArchiveFile suite,
        SiteIsolatedGitHubArchiveFile provider, SiteIsolatedGitHubArchiveFile[] files)
    {
        var value = metadata.DeepClone().AsObject();
        value[SiteIsolatedGitHubTokens.State] = SiteIsolatedGitHubTokens.ArchiveState;
        value[SiteIsolatedGitHubTokens.Archives] = new JsonObject
        {
            [SiteIsolatedGitHubTokens.SuiteKey] = JsonSerializer.SerializeToNode(suite, Options),
            [SiteIsolatedGitHubTokens.ProviderKey] = JsonSerializer.SerializeToNode(provider, Options),
        };
        value[SiteIsolatedGitHubTokens.InputFiles] = JsonSerializer.SerializeToNode(files, Options);
        return value;
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

    private static async Task RequireArchivesUnchangedAsync(string capture, JsonObject metadata,
        SiteIsolatedGitHubArchiveFile suite, SiteIsolatedGitHubArchiveFile provider, CancellationToken token)
    {
        var actualSuite = await SiteIsolatedGitHubArchiveReader.VerifyArchiveAsync(Path.Combine(capture,
                SiteIsolatedGitHubTokens.Archives, SiteIsolatedGitHubTokens.Suite), SiteIsolatedGitHubTokens.Suite,
            metadata, SiteIsolatedGitHubTokens.SuiteKey, SiteIsolatedGitHubTokens.SuiteBytes, token);
        var actualProvider = await SiteIsolatedGitHubArchiveReader.VerifyArchiveAsync(Path.Combine(capture,
                SiteIsolatedGitHubTokens.Archives, SiteIsolatedGitHubTokens.Provider), SiteIsolatedGitHubTokens.Provider,
            metadata, SiteIsolatedGitHubTokens.ProviderKey, SiteIsolatedGitHubTokens.ProviderBytes, token);
        if (actualSuite != suite || actualProvider != provider)
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Changed);
        }
    }
}
