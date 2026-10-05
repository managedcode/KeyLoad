using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace KeyLoad.Comparisons;

internal static class OpenLoopEvidenceArtifactWriter
{
    internal static async Task<string> WriteAsync<TArtifact>(string pending, string final, TArtifact artifact,
        int maximumBytes, int writerBufferBytes, CancellationToken cancellationToken)
    {
        FileStream? output = null;
        var ownsPending = false;
        try
        {
            output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                writerBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
            ownsPending = true;
            await using (var bounded = new BoundedEvidenceStream(output, maximumBytes))
            {
                await JsonSerializer.SerializeAsync(bounded, artifact, ReportWriter.JsonOptions, cancellationToken)
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
