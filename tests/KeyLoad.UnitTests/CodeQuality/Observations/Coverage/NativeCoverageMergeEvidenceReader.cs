using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageMergeEvidenceReader
{
    private const int ProofMaximumDepth = 12;
    private const string InvalidProofMessage = "The native coverage tooling proof is missing or exceeds its bound.";

    internal static async Task<JsonDocument> ReadProofAsync(string path,
        NativeCoverageExecutionOptions options, CancellationToken cancellationToken)
    {
        var before = new FileInfo(path);
        if (!before.Exists || before.LinkTarget is not null || before.Length <= 0
            || before.Length > options.MaximumFileBytes)
        {
            throw new InvalidDataException(InvalidProofMessage);
        }
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            options.ReadBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length != before.Length)
        {
            throw new InvalidDataException(InvalidProofMessage);
        }
        var proof = await JsonDocument.ParseAsync(input,
            new JsonDocumentOptions { MaxDepth = ProofMaximumDepth }, cancellationToken).ConfigureAwait(false);
        var after = new FileInfo(path);
        if (input.Length != before.Length || after.Length != before.Length
            || after.LastWriteTimeUtc != before.LastWriteTimeUtc)
        {
            proof.Dispose();
            throw new InvalidDataException(InvalidProofMessage);
        }
        return proof;
    }
}
