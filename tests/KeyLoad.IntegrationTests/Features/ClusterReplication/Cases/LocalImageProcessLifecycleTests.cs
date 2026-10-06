using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication.Cases;

internal sealed class LocalImageProcessLifecycleTests
{
    private const string Tag = "local-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Receipt = "TestResults/rf3/local-images/image-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.json";
    private const string OverflowMessage = "Local RF3 image verifier output exceeded its bound.";

    [Test]
    public async Task VerifierStopsActualNodeProcessWhenBoundedReaderOverflowsBeforeExit()
    {
        await LocalImageTestDirectory.RunAsync("keyload-local-image-verify-overflow-", async root =>
        {
            var script = Path.Combine(root, "overflow.mjs");
            var terminated = Path.Combine(root, "terminated");
            await File.WriteAllTextAsync(script, ProcessProgram(null, terminated, writeOverflow: true));
            var failure = await CaptureFailureAsync(RunVerifierAsync(root, script, CancellationToken.None));

            await Assert.That(ContainsMessage(failure, OverflowMessage)).IsTrue();
            await Assert.That(File.Exists(terminated)).IsTrue();
        });
    }

    [Test]
    public async Task VerifierStopsActualNodeProcessAndPreservesCallerCancellation()
    {
        await LocalImageTestDirectory.RunAsync("keyload-local-image-verify-cancel-", async root =>
        {
            var script = Path.Combine(root, "wait.mjs");
            var ready = Path.Combine(root, "ready");
            var terminated = Path.Combine(root, "terminated");
            await File.WriteAllTextAsync(script, ProcessProgram(ready, terminated, writeOverflow: false));
            using var cancellation = new CancellationTokenSource();
            var verification = RunVerifierAsync(root, script, cancellation.Token);
            using var startupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10), TimeProvider.System);
            Exception? failure;
            try
            {
                await WaitForMarkerAsync(ready, verification, startupTimeout.Token);
                await cancellation.CancelAsync();
                failure = await CaptureFailureAsync(verification);
            }
            finally
            {
                await cancellation.CancelAsync();
                if (!verification.IsCompleted)
                {
                    _ = await CaptureFailureAsync(verification);
                }
            }

            await Assert.That(ContainsCancellation(failure)).IsTrue();
            await Assert.That(File.Exists(terminated)).IsTrue();
        });
    }

    private static Task<string> RunVerifierAsync(string root, string script, CancellationToken cancellationToken)
        => LocalRf3ImageIdentity.RunVerifierAsync(root, script, Tag, Receipt, cancellationToken);

    private static string ProcessProgram(string? ready, string terminated, bool writeOverflow)
        => "import { writeFileSync } from 'node:fs';\n"
            + "process.on('SIGTERM', () => { writeFileSync(" + JsonSerializer.Serialize(terminated)
            + ", 'term'); process.exit(0); });\n"
            + (ready is null ? string.Empty : "writeFileSync(" + JsonSerializer.Serialize(ready) + ", 'ready');\n")
            + (writeOverflow ? "process.stdout.write('x'.repeat(20000));\n" : string.Empty)
            + "setInterval(() => {}, 1000);\n";

    private static async Task WaitForMarkerAsync(string marker, Task operation, CancellationToken cancellationToken)
    {
        if (File.Exists(marker))
        {
            return;
        }

        var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(Path.GetDirectoryName(marker)!, Path.GetFileName(marker));
        watcher.Created += (_, _) => source.TrySetResult(true);
        watcher.EnableRaisingEvents = true;
        if (File.Exists(marker))
        {
            source.TrySetResult(true);
        }

        var canceled = Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, cancellationToken);
        var completed = await Task.WhenAny(source.Task, operation, canceled).ConfigureAwait(false);
        if (completed == canceled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (completed == operation)
        {
            await operation.ConfigureAwait(false);
            throw new InvalidOperationException("The native verifier child exited before its readiness marker.");
        }
        await source.Task.ConfigureAwait(false);
    }

    private static Task<Exception?> CaptureFailureAsync(Task operation)
        => KeyLoad.IntegrationTests.Features.ClusterReplication.Processes.OwnedProcessFailureObserver.CaptureAsync(operation);

    private static bool ContainsCancellation(Exception? exception)
        => exception is OperationCanceledException || exception is AggregateException aggregate
            && aggregate.InnerExceptions.Any(ContainsCancellation);

    private static bool ContainsMessage(Exception? exception, string message)
        => exception is not null && (exception.Message == message
            || exception is AggregateException aggregate
                && aggregate.InnerExceptions.Any(inner => ContainsMessage(inner, message)));
}
