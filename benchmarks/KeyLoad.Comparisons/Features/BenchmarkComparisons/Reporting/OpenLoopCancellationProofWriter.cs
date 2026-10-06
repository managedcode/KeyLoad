using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Atomically publishes the bounded actual-work cancellation proof.</summary>
public static class OpenLoopCancellationProofWriter
{
    /// <summary>Writes a validated proof once without replacing any existing artifact.</summary>
    /// <param name="directory">The already-owned output directory.</param>
    /// <param name="proof">The completed cancellation proof.</param>
    /// <param name="executionOptions">The centrally validated native report buffering policy.</param>
    /// <param name="cancellationToken">The uncancelled host token.</param>
    /// <returns>The final proof path after atomic publication.</returns>
    public static async Task<string> WriteAsync(string directory, OpenLoopCancellationProofV1 proof,
        IOptions<NativeComparisonExecutionOptions> executionOptions, CancellationToken cancellationToken)
    {
        var execution = NativeComparisonExecutionOptions.Require(executionOptions).Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        OpenLoopCancellationProofValidation.Validate(proof);
        var final = Path.Combine(directory, OpenLoopCancellationProofContract.ProofFileName);
        var pending = Path.Combine(directory, OpenLoopCancellationProofContract.PendingProofFileName);
        return await OpenLoopEvidenceArtifactWriter.WriteAsync(pending, final, proof,
            OpenLoopCancellationProofContract.MaximumProofBytes,
            execution.OpenLoopProofBufferBytes, cancellationToken).ConfigureAwait(false);
    }
}
