using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Collections.Immutable;

namespace KeyLoad.Comparisons;

/// <summary>Writes the separate bounded open-loop artifact without touching the closed-loop report.</summary>
public static class OpenLoopEvidenceWriter
{
    private const int MaximumBytes = 4 * 1024 * 1024;
    private const string FileName = "open-loop-evidence.v1.json";
    private const string PendingFileName = ".open-loop-evidence.v1.json.pending";

    /// <summary>Creates the unique versioned open-loop artifact in a caller-owned evidence directory.</summary>
    public static async Task<string> WriteAsync(string directory, OpenLoopComparisonReport report,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(report);
        OpenLoopEvidenceValidation.Validate(report);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName);
        var pending = Path.Combine(directory, PendingFileName);
        FileStream? output = null;
        var ownsPending = false;
        try
        {
            output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16_384, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
            ownsPending = true;
            await using (var bounded = new BoundedEvidenceStream(output, MaximumBytes))
            {
                await JsonSerializer.SerializeAsync(bounded, report, ReportWriter.JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await bounded.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            await output.DisposeAsync().ConfigureAwait(false);
            output = null;
            File.Move(pending, path, overwrite: false);
            return path;
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
