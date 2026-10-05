using System.Diagnostics;
using System.Globalization;
using System.Text;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class RequestCqrsOfflineProcessOwnedChild : IAsyncDisposable
{
    private readonly System.Threading.Lock disposalGate = new();
    private readonly ProcessStartInfo start;
    private readonly CancellationTokenSource cancellation;
    private readonly Task<NodeEpochRf3OfflineResult> operation;
    private Process? process;
    private Task? disposalTask;
    private bool observed;

    internal RequestCqrsOfflineProcessOwnedChild(ProcessStartInfo start, CancellationToken parentToken)
    {
        this.start = start;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        operation = NodeEpochRf3OfflineProcess.RunAsync(start, cancellation.Token);
    }

    internal bool IsSettled => operation.IsCompleted;

    internal async Task<NodeEpochRf3OfflineResult> ObserveResultAsync()
    {
        try
        { return await operation.ConfigureAwait(false); }
        finally
        { observed = true; }
    }

    internal Task CancelAsync() => cancellation.CancelAsync();

    internal async Task ReleaseOutputAsync()
    {
        var path = start.ArgumentList[6];
        var bytes = new UTF8Encoding(false, true).GetBytes(RequestCqrsOfflineProcessProtocol.ReleaseSignal);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            RequestCqrsOfflineProcessProtocol.MarkerBytes, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await output.WriteAsync(bytes, cancellation.Token).ConfigureAwait(false);
        await output.FlushAsync(cancellation.Token).ConfigureAwait(false);
    }

    internal async Task<Process> WaitForStartedAsync()
    {
        var marker = ArgumentForMarker(start);
        var nonce = ArgumentForNonce(start);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        deadline.CancelAfter(RequestCqrsOfflineProcessProtocol.MarkerDeadline);
        while (!File.Exists(marker))
        { await Task.Delay(TimeSpan.FromMilliseconds(20), deadline.Token).ConfigureAwait(false); }
        var contents = await ReadMarkerAsync(marker, deadline.Token).ConfigureAwait(false);
        var values = contents.Split(':', StringSplitOptions.None);
        if (values.Length != 2 || values[1] != nonce
            || !int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0)
        { throw new InvalidDataException(RequestCqrsOfflineProcessProtocol.InvalidMarker); }
        process = Process.GetProcessById(pid);
        if (process.HasExited || process.ProcessName != RequestCqrsOfflineProcessProtocol.Executable)
        { throw new InvalidDataException(RequestCqrsOfflineProcessProtocol.InvalidMarker); }
        return process;
    }

    internal async Task<OperationCanceledException> ExpectCancellationAsync()
    {
        try
        {
            var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
            return failure ?? throw new InvalidOperationException(RequestCqrsOfflineProcessProtocol.MissingFailure);
        }
        finally
        { observed = true; }
    }

    internal async Task<InvalidOperationException> ExpectOutputLimitAsync()
    {
        try
        {
            var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => operation);
            return failure ?? throw new InvalidOperationException(RequestCqrsOfflineProcessProtocol.MissingFailure);
        }
        finally
        { observed = true; }
    }

    internal RequestCqrsOfflineProcessChildView Borrow() => new(this);

    public ValueTask DisposeAsync()
    {
        lock (disposalGate)
        {
            disposalTask ??= DisposeCoreAsync();
            return new(disposalTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        if (!operation.IsCompleted)
        { await ServerFailureObserver.ObserveAsync(cancellation.CancelAsync, failures).ConfigureAwait(false); }
        try
        {
            if (!observed)
            {
                var operationFailures = new List<Exception>();
                await ServerFailureObserver.ObserveAsync(() => operation, operationFailures).ConfigureAwait(false);
                if (operation.IsCanceled && cancellation.IsCancellationRequested)
                { operationFailures.RemoveAll(failure => failure is OperationCanceledException); }
                failures.AddRange(operationFailures);
            }
        }
        finally
        {
            if (operation.IsCompleted)
            {
                DisposeProcess(failures);
                DisposeCancellation(failures);
            }
        }
        if (!operation.IsCompleted)
        { throw new InvalidOperationException(RequestCqrsOfflineProcessProtocol.MarkerTimeout); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void DisposeProcess(List<Exception> failures)
    {
        try
        { process?.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
    }

    private void DisposeCancellation(List<Exception> failures)
    {
        try
        { cancellation.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
    }

    private static async Task<string> ReadMarkerAsync(string path, CancellationToken cancellationToken)
    {
        await using var input = NodeEpochRf3OfflineFiles.OpenRead(path);
        if (input.Length is < 1 or > RequestCqrsOfflineProcessProtocol.MarkerBytes)
        { throw new InvalidDataException(RequestCqrsOfflineProcessProtocol.InvalidMarker); }
        var count = checked((int)input.Length);
        var bytes = new byte[RequestCqrsOfflineProcessProtocol.MarkerBytes];
        await input.ReadExactlyAsync(bytes.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        if (input.ReadByte() != -1)
        { throw new InvalidDataException(RequestCqrsOfflineProcessProtocol.InvalidMarker); }
        return Encoding.ASCII.GetString(bytes, 0, count);
    }

    private static string ArgumentForMarker(ProcessStartInfo start) => start.ArgumentList[3];
    private static string ArgumentForNonce(ProcessStartInfo start) => start.ArgumentList[4];
}
