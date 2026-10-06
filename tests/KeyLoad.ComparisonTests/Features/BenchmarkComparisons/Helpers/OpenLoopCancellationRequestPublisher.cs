using System.Runtime.ExceptionServices;
using System.Text;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class OpenLoopCancellationRequestPublisher
{
    private const int RequestBufferBytes = 32;
    private const string PublishFailureCode = "OpenLoopCancellationRequestPublishFailed";
    internal static void Publish(string output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        var pending = Path.Combine(output, OpenLoopCancellationProofContract.PendingRequestFileName);
        var request = Path.Combine(output, OpenLoopCancellationProofContract.RequestFileName);
        FileStream? stream = null;
        var ownsPending = false;
        try
        {
            stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                RequestBufferBytes, FileOptions.WriteThrough);
            ownsPending = true;
            var bytes = Encoding.UTF8.GetBytes(OpenLoopCancellationProofContract.RequestText);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            stream.Dispose();
            stream = null;
            File.Move(pending, request, overwrite: false);
        }
        catch (Exception failure)
        {
            var cleanup = new List<Exception>();
            try { stream?.Dispose(); }
            catch (Exception error) { cleanup.Add(error); }
            if (ownsPending && File.Exists(pending))
            {
                try { File.Delete(pending); }
                catch (Exception error) { cleanup.Add(error); }
            }
            var all = new[] { failure }.Concat(cleanup).ToArray();
            if (all.Length == 1) ExceptionDispatchInfo.Capture(failure).Throw();
            throw new AggregateException(PublishFailureCode, all);
        }
    }
}
