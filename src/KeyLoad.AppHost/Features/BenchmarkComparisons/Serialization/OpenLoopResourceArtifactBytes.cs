using KeyLoad.Comparisons;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class OpenLoopResourceArtifactBytes
{
    private const int EmptyArtifactBytes = 0;
    private const int InitialReadOffset = 0;
    private const int EndOfFileReadCount = 0;

    internal static async Task<byte[]> ReadAsync(string path, int bufferBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var length = input.Length;
            if (length is <= EmptyArtifactBytes or > OpenLoopEvidenceContract.MaximumArtifactBytes)
            {
                throw InvalidArtifact();
            }

            var bytes = new byte[checked((int)length)];
            var read = InitialReadOffset;
            while (read < bytes.Length)
            {
                var count = await input.ReadAsync(bytes.AsMemory(read), cancellationToken).ConfigureAwait(false);
                if (count == EndOfFileReadCount)
                {
                    throw InvalidArtifact();
                }
                read = checked(read + count);
            }

            var excess = new byte[OpenLoopResourceEvidenceContract.ExcessArtifactProbeBytes];
            if (await input.ReadAsync(excess, cancellationToken).ConfigureAwait(false) != EndOfFileReadCount)
            {
                throw InvalidArtifact();
            }
            return bytes;
        }
        catch (IOException)
        {
            throw InvalidArtifact();
        }
        catch (UnauthorizedAccessException)
        {
            throw InvalidArtifact();
        }
    }

    private static InvalidDataException InvalidArtifact()
        => new(OpenLoopResourceEvidenceContract.InvalidSourceArtifact);
}
