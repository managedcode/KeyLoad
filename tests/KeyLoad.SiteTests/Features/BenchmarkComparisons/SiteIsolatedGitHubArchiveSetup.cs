using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteIsolatedGitHubArchiveSetup
{
    public static async Task<SiteIsolatedGitHubArchiveReceipt> PrepareFromEnvironmentAsync(CancellationToken token)
    {
        var capture = SiteIsolatedGitHubInputs.Required(SiteIsolatedGitHubTokens.CaptureEnvironment);
        var path = SiteIsolatedGitHubInputs.Required(SiteIsolatedGitHubTokens.ReceiptEnvironment);
        var receipt = await PrepareAsync(capture, path, token);
        Environment.SetEnvironmentVariable(SiteIsolatedGitHubTokens.AggregateEnvironment,
            Path.Combine(capture, SiteIsolatedGitHubTokens.Input, SiteIsolatedGitHubTokens.Aggregate));
        return receipt;
    }

    public static async Task<SiteIsolatedGitHubArchiveReceipt> PrepareAsync(string capture,
        string receiptPath, CancellationToken token)
    {
        SiteIsolatedGitHubFileOperations.RequireSafeAncestors(receiptPath);
        if (receiptPath != Path.Combine(capture, SiteIsolatedGitHubTokens.Receipt))
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.InvalidPath);
        }

        SiteIsolatedGitHubFileOperations.RequireAbsent(receiptPath);
        SiteIsolatedGitHubFileOperations.RequireAbsent(Path.Combine(capture, SiteIsolatedGitHubTokens.Input));
        var metadata = await SiteIsolatedGitHubReceiptReader.ReadMetadataAsync(capture, token);
        return await SiteIsolatedGitHubArchiveReader.ExtractAsync(capture, receiptPath, metadata, token);
    }

    public static async Task VerifyUnchangedAsync(SiteIsolatedGitHubArchiveReceipt receipt, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        SiteIsolatedGitHubReceiptReader.RequireCallerSources(receipt.Value);
        var value = JsonNode.Parse(await File.ReadAllBytesAsync(receipt.ReceiptPath, token));
        if (!receipt.Matches(value))
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Changed);
        }

        foreach (var file in receipt.Files)
        {
            var actual = await SiteIsolatedGitHubFileOperations.HashAsync(
                SiteIsolatedGitHubFileOperations.FixedTarget(receipt.Capture, file.Path), file.Path, file.Bytes, token);
            if (actual != file)
            {
                throw new InvalidDataException(SiteIsolatedGitHubTokens.Changed);
            }
        }

        await VerifyInputsAsync(receipt, captureCoverage: true, token);
    }

    internal static async Task VerifyInputsAsync(SiteIsolatedGitHubArchiveReceipt receipt,
        bool captureCoverage, CancellationToken token)
    {
        var repository = SiteIsolatedGitHubInputs.Required(SiteTokens.RepositoryEnvironment);
        var result = await SiteIsolatedGitHubNodeProcess.RunAsync(repository, new
        {
            operation = SiteIsolatedGitHubFields.InputsOperation,
            repository,
            arguments = new { input = receipt.Capture, receipt = receipt.ReceiptPath },
        }, token, captureCoverage);
        if (!result.GetProperty(SiteIsolatedGitHubFields.Ok).GetBoolean())
        {
            throw new InvalidDataException(SiteIsolatedGitHubTokens.Changed);
        }
    }
}
