using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting.Processes;

internal sealed record C1OutcomeInspectionProcessResult(
    int ProcessId,
    int ExitCode,
    byte[] StandardOutput,
    byte[] StandardError,
    bool StandardOutputExceeded,
    bool StandardErrorExceeded,
    bool ProcessReaped,
    bool InputWriterSettled,
    bool StandardOutputReaderSettled,
    bool StandardErrorReaderSettled,
    bool ProcessHandleClosed,
    bool OuterOwnerReleased);

internal static class C1OutcomeInspectionProcess
{
    private const int MaximumInputBytes = 8193;

    internal static async Task<C1OutcomeInspectionProcessResult> RunAsync(
        ReadOnlyMemory<byte> input, string outerOwnerLockPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outerOwnerLockPath);
        if (input.Length is 0 or > MaximumInputBytes)
        { throw new ArgumentOutOfRangeException(nameof(input)); }
        cancellationToken.ThrowIfCancellationRequested();
        var failures = new List<Exception>();
        C1OutcomeInspectionProcessResult? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var lifetime = new C1OutcomeInspectionProcessLifetime(input, outerOwnerLockPath, failures);
            await ServerFailureObserver.ObserveAsync(() => lifetime.RunAsync(cancellationToken), failures).ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(() => lifetime.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            if (!lifetime.DisposalAttemptCompleted)
            {
                await ServerFailureObserver.ObserveAsync(() => lifetime.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            }
            result = lifetime.Result;
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(MissingResultMessage);
    }

    private const string MissingResultMessage = "The outcome inspection did not produce a settled process result.";
}
