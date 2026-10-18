using System.Diagnostics;
using System.Text;
using KeyLoad.RecoveryHost;
using KeyLoad.RecoveryHost.Features.Messaging;
using KeyLoad.IntegrationTests.Features.ClusterReplication.Processes;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueWorkerClusterChild : IAsyncDisposable
{
    private const string Runtime = "dotnet";
    private const int BufferCharacters = 1024;
    private const char NewLine = '\n';
    private readonly Process process;
    private Task? output;
    private Task<string>? error;
    private Task? exit;
    private Task? disposal;
    private bool started;
    private readonly TaskCompletionSource<ExternalQueueWorkerCrashClaim> claimed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Exception> failures = [];

    private QueueWorkerClusterChild(Process actual) => process = actual;

    private void InitializeReaders()
    {
        exit = process.WaitForExitAsync();
        output = ReadOutputAsync(process.StandardOutput);
        error = LocalImageOwnedProcessLifetime.ReadBoundedAsync(process.StandardError,
            ExternalQueueWorkerCrashProtocol.MaximumFrameCharacters);
    }

    internal static async Task<QueueWorkerClusterChild> StartAsync(ExternalQueueWorkerCrashInput input, CancellationToken token)
    {
        var start = new ProcessStartInfo(Runtime)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(typeof(RecoveryHostMarker).Assembly.Location);
        start.ArgumentList.Add(ExternalQueueWorkerCrashProtocol.Mode);
        var child = new QueueWorkerClusterChild(new Process { StartInfo = start });
        try
        {
            if (!child.process.Start())
            {
                throw new InvalidOperationException(QueueWorkerClusterProtocol.Missing);
            }
            child.started = true;
            child.InitializeReaders();
            var frame = Convert.ToBase64String(NativeSerialization.Serialize(input));
            await child.process.StandardInput.WriteAsync(frame.AsMemory(), token);
            child.process.StandardInput.Close();
            return child;
        }
        catch (Exception primary)
        {
            var startupFailures = new List<Exception> { primary };
            await ServerFailureObserver.ObserveAsync(() => child.DisposeAsync().AsTask(), startupFailures);
            ServerFailureObserver.ThrowIfAny(startupFailures);
            throw;
        }
    }

    internal async Task<ExternalQueueWorkerCrashClaim> WaitClaimAsync(CancellationToken token)
    {
        var selected = await Task.WhenAny(claimed.Task, exit!, output!, error!).WaitAsync(token);
        if (selected == claimed.Task)
        {
            return await claimed.Task;
        }
        await selected;
        throw new InvalidOperationException(QueueWorkerClusterProtocol.Missing);
    }

    internal async Task KillAndJoinAsync()
    {
        process.Kill(entireProcessTree: true);
        await Task.WhenAll(exit!, output!, error!);
        await Assert.That(process.HasExited).IsTrue();
        await Assert.That(await error!).IsEqualTo(string.Empty);
    }

    private async Task ReadOutputAsync(StreamReader reader)
    {
        var buffer = new char[BufferCharacters];
        var frame = new StringBuilder();
        var published = false;
        while (true)
        {
            var read = await reader.ReadAsync(buffer);
            if (read == ExternalQueueWorkerCrashProtocol.EndOfInput)
            {
                return;
            }
            frame.Append(buffer, ExternalQueueWorkerCrashProtocol.BufferStart, read);
            if (frame.Length > ExternalQueueWorkerCrashProtocol.MaximumFrameCharacters)
            {
                throw new InvalidOperationException(ExternalQueueWorkerCrashProtocol.InvalidInput);
            }
            var text = frame.ToString();
            var end = text.IndexOf(NewLine, StringComparison.Ordinal);
            if (!published && end >= ExternalQueueWorkerCrashProtocol.BufferStart)
            {
                var line = text[..end].TrimEnd();
                if (!line.StartsWith(ExternalQueueWorkerCrashProtocol.Marker, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(QueueWorkerClusterProtocol.Missing);
                }
                claimed.TrySetResult(NativeSerialization.Deserialize<ExternalQueueWorkerCrashClaim>(Convert.FromBase64String(
                    line[ExternalQueueWorkerCrashProtocol.Marker.Length..])));
                published = true;
            }
        }
    }

    public ValueTask DisposeAsync() => new(disposal ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        if (started)
        {
            await ServerFailureObserver.ObserveAsync(() => LocalImageOwnedProcessLifetime.TerminateAndJoinAsync(
                process, exit, output, error, failures), failures);
        }
        ServerFailureObserver.Observe(process.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
