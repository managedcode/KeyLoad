using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace KeyLoad.Comparisons;

/// <summary>Atomically publishes the bounded actual-work cancellation proof.</summary>
public static class OpenLoopCancellationProofWriter
{
    private const string PendingFileName = ".open-loop-cancellation-proof.v1.json.pending";

    /// <summary>Writes a validated proof once without replacing any existing artifact.</summary>
    /// <param name="directory">The already-owned output directory.</param>
    /// <param name="proof">The completed cancellation proof.</param>
    /// <param name="cancellationToken">The uncancelled host token.</param>
    /// <returns>The final proof path after atomic publication.</returns>
    public static async Task<string> WriteAsync(string directory, OpenLoopCancellationProofV1 proof,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        OpenLoopCancellationProofValidation.Validate(proof);
        var final = Path.Combine(directory, OpenLoopCancellationProofContract.ProofFileName);
        var pending = Path.Combine(directory, PendingFileName);
        FileStream? output = null;
        var ownsPending = false;
        try
        {
            output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                8_192, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
            ownsPending = true;
            await using (var bounded = new BoundedEvidenceStream(output,
                OpenLoopCancellationProofContract.MaximumProofBytes))
            {
                await JsonSerializer.SerializeAsync(bounded, proof, ReportWriter.JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await bounded.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            await output.DisposeAsync().ConfigureAwait(false);
            output = null;
            File.Move(pending, final, overwrite: false);
            return final;
        }
        catch (Exception failure)
        {
            var cleanup = ImmutableArray.CreateBuilder<Exception>();
            if (output is not null)
            {
                try { await output.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) { cleanup.Add(error); }
            }
            if (ownsPending && File.Exists(pending))
            {
                try { File.Delete(pending); }
                catch (Exception error) { cleanup.Add(error); }
            }
            ExceptionDispatchInfo.Capture(OpenLoopFailure.Combine(failure, cleanup.ToImmutable())!).Throw();
            throw;
        }
    }
}
