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
            await File.WriteAllTextAsync(script, OverflowProgram(terminated));
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
            var files = await LocalImageVerifierProcessProof.WriteAsync(root, LocalImageVerifierProcessProof.WaitMode)
                .ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            var verification = RunVerifierAsync(root, files.Script, cancellation.Token);
            using var startupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10), TimeProvider.System);
            Exception? failure;
            try
            {
                await LocalImageVerifierProcessProof.WaitForMarkerAsync(files.ProcessId, verification, startupTimeout.Token);
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
            await LocalImageVerifierProcessProof.AssertStoppedAsync(files, LocalImageVerifierProcessProof.Terminated)
                .ConfigureAwait(false);
        });
    }

    private static Task<string> RunVerifierAsync(string root, string script, CancellationToken cancellationToken)
        => LocalRf3ImageIdentity.RunVerifierAsync(root, script, Tag, Receipt, cancellationToken);

    private static string OverflowProgram(string terminated)
        => "import { writeFileSync } from 'node:fs';\n"
            + "process.on('SIGTERM', () => { writeFileSync(" + JsonSerializer.Serialize(terminated)
            + ", 'term'); process.exit(0); });\n"
            + "process.stdout.write('x'.repeat(20000));\n"
            + "setInterval(() => {}, 1000);\n";

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
