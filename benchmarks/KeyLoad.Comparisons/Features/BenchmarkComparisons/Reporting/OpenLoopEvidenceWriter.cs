using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Writes the separate bounded open-loop artifact without touching the closed-loop report.</summary>
public static class OpenLoopEvidenceWriter
{
    /// <summary>Creates the unique versioned open-loop artifact in a caller-owned evidence directory.</summary>
    public static async Task<string> WriteAsync(string directory, OpenLoopComparisonReport report,
        IOptions<NativeComparisonExecutionOptions> executionOptions, CancellationToken cancellationToken)
    {
        var execution = NativeComparisonExecutionOptions.Require(executionOptions).Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(report);
        OpenLoopEvidenceValidation.Validate(report);
        Directory.CreateDirectory(directory);
        var final = Path.Combine(directory, OpenLoopEvidenceContract.OpenLoopEvidenceFileName);
        var pending = Path.Combine(directory, OpenLoopEvidenceContract.PendingOpenLoopEvidenceFileName);
        return await OpenLoopEvidenceArtifactWriter.WriteAsync(pending, final, report,
            OpenLoopEvidenceContract.MaximumArtifactBytes, execution.OpenLoopEvidenceBufferBytes,
            cancellationToken).ConfigureAwait(false);
    }
}
