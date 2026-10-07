using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Observes an actual owned verifier child and its terminal state; it supplies no image identity.</summary>
internal static class LocalImageVerifierProcessProof
{
    internal const string ExpectedOutput = "bounded-native-output";
    internal const string VerificationFailure = "The local RF3 image could not be verified.";
    internal const string SuccessMode = "success";
    internal const string ErrorMode = "error";
    internal const string WaitMode = "wait";
    internal const string Finished = "finished";
    internal const string Terminated = "SIGTERM";
    private const string ScriptName = "owned-verifier.mjs";
    private const string ProcessIdName = "process-id";
    private const string StoppedName = "stopped";
    private const string EmptyText = "";
    private const string EarlyExit = "The native verifier child exited before its readiness marker.";
    private const string NativeError = "native-stderr";

    internal sealed record Files(string Script, string ProcessId, string Stopped);

    internal static async Task<Files> WriteAsync(string root, string mode)
    {
        var files = new Files(Path.Combine(root, ScriptName), Path.Combine(root, ProcessIdName),
            Path.Combine(root, StoppedName));
        var program = "import { writeFileSync } from 'node:fs';\n"
            + "const pidFile = " + JsonSerializer.Serialize(files.ProcessId) + ";\n"
            + "const stoppedFile = " + JsonSerializer.Serialize(files.Stopped) + ";\n"
            + "const mode = " + JsonSerializer.Serialize(mode) + ";\n"
            + "process.on('SIGTERM', () => process.stdout.write('terminal-stdout', () => "
            + "process.stderr.write('terminal-stderr', () => { writeFileSync(stoppedFile, 'SIGTERM'); process.exit(0); })));\n"
            + "writeFileSync(pidFile, String(process.pid));\n"
            + "if (mode === 'wait') { setInterval(() => {}, 1000); }\n"
            + "else { process.stdout.write(" + JsonSerializer.Serialize(ExpectedOutput) + ", () => "
            + "process.stderr.write(mode === 'error' ? " + JsonSerializer.Serialize(NativeError) + " : "
            + JsonSerializer.Serialize(EmptyText)
            + ", () => { writeFileSync(stoppedFile, 'finished'); process.exit(0); })); }\n";
        await File.WriteAllTextAsync(files.Script, program).ConfigureAwait(false);
        return files;
    }

    internal static async Task AssertStoppedAsync(Files files, string expectedState)
    {
        await Assert.That(await File.ReadAllTextAsync(files.Stopped).ConfigureAwait(false)).IsEqualTo(expectedState);
        var processId = int.Parse(await File.ReadAllTextAsync(files.ProcessId).ConfigureAwait(false),
            NumberStyles.None, CultureInfo.InvariantCulture);
        await Assert.That(IsRunning(processId)).IsFalse();
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            using var child = Process.GetProcessById(processId);
            return !child.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    internal static async Task WaitForMarkerAsync(string marker, Task operation, CancellationToken cancellationToken)
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
            throw new InvalidOperationException(EarlyExit);
        }
        await source.Task.ConfigureAwait(false);
    }

    internal static bool ContainsCancellation(Exception? exception)
        => exception is OperationCanceledException || exception is AggregateException aggregate
            && aggregate.InnerExceptions.Any(ContainsCancellation);

    internal static bool ContainsMessage(Exception? exception, string message)
        => exception is not null && (exception.Message == message
            || exception is AggregateException aggregate
                && aggregate.InnerExceptions.Any(inner => ContainsMessage(inner, message)));
}
