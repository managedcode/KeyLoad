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
                RandomAccess.FlushToDisk(output.SafeFileHandle);
            }
            await output.DisposeAsync().ConfigureAwait(false);
            output = null;
            File.Move(pending, final, overwrite: false);
            return final;
        }
        catch (Exception failure)
        {
            var combined = await SettleFailedWriteAsync(output, pending, ownsPending, failure).ConfigureAwait(false);
            ExceptionDispatchInfo.Capture(combined).Throw();
            throw;
        }
    }

    private static async Task<Exception> SettleFailedWriteAsync(FileStream? output, string pending,
        bool ownsPending, Exception failure)
    {
        var cleanup = ImmutableArray.CreateBuilder<Exception>();
        if (output is not null)
        {
            var disposalFailure = await OpenLoopFailure.ObserveAsync(DisposeOutputAsync(output)).ConfigureAwait(false);
            if (disposalFailure is not null)
            {
                cleanup.Add(disposalFailure);
            }
        }
        if (ownsPending)
        {
            try
            {
                if (File.Exists(pending))
                {
                    File.Delete(pending);
                }
            }
            catch (Exception error)
            {
                cleanup.Add(error);
                ExceptionDispatchInfo.Capture(OpenLoopFailure.Combine(failure, cleanup.ToImmutable())!).Throw();
                throw;
            }
        }
        return OpenLoopFailure.Combine(failure, cleanup.ToImmutable())!;
    }

    private static async Task DisposeOutputAsync(FileStream output)
    {
        await output.DisposeAsync().ConfigureAwait(false);
    }
}
