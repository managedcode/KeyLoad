using System.Text;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class OpenLoopCancellationRequestPublisher
{
    private const string PublishFailureCode = "OpenLoopCancellationRequestPublishFailed";
    internal static async Task PublishAsync(string output, IOptions<NativeComparisonHarnessOptions> executionOptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        ArgumentNullException.ThrowIfNull(executionOptions);
        var execution = executionOptions.Value;
        execution.Validate();
        var pending = Path.Combine(output, OpenLoopCancellationProofContract.PendingRequestFileName);
        var request = Path.Combine(output, OpenLoopCancellationProofContract.RequestFileName);
        var ownsPending = false;
        var original = WriteAndMoveAsync(pending, request, execution.CancellationRequestFileBufferBytes,
            () => ownsPending = true);
        var failure = await OpenLoopFailure.ObserveAsync(original);
        if (failure is not null)
        {
            var failures = new IsolatedNativeTeardownFailures(failure);
            var cleanup = new List<Exception>();
            await IsolatedNativeTeardownNativeSupport.CollectFailureAsync(() =>
            {
                if (ownsPending && File.Exists(pending))
                { File.Delete(pending); }
                return Task.CompletedTask;
            }, cleanup);
            foreach (var cleanupFailure in cleanup)
            { failures.Record(PublishFailureCode, cleanupFailure); }
            failures.ThrowIfAny();
        }
    }

    private static async Task WriteAndMoveAsync(string pending, string request, int bufferBytes,
        Action markPendingOwned)
    {
        await WritePendingAsync(pending, bufferBytes, markPendingOwned);
        File.Move(pending, request, overwrite: false);
    }

    private static async Task WritePendingAsync(string pending, int bufferBytes, Action markPendingOwned)
    {
        var failures = new List<Exception>();
        await IsolatedNativeTeardownNativeSupport.CollectFailureAsync(WriteOwnedAsync, failures);
        ThrowWriteFailures(failures);

        async Task WriteOwnedAsync()
        {
            await using var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferBytes, FileOptions.WriteThrough);
            markPendingOwned();
            await IsolatedNativeTeardownNativeSupport.CollectFailureAsync(() => WriteBytesAsync(stream), failures);
        }
    }

    private static async Task WriteBytesAsync(FileStream stream)
    {
        var bytes = Encoding.UTF8.GetBytes(OpenLoopCancellationProofContract.RequestText);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
        SyncFlushToDisk(stream);
    }

    private static void SyncFlushToDisk(FileStream stream)
        => stream.Flush(flushToDisk: true);

    private static void ThrowWriteFailures(List<Exception> failures)
    {
        if (failures.Count == 0)
        { return; }
        var observed = new IsolatedNativeTeardownFailures(failures[0]);
        for (var index = 1; index < failures.Count; index++)
        { observed.Record(PublishFailureCode, failures[index]); }
        observed.ThrowIfAny();
    }

}
