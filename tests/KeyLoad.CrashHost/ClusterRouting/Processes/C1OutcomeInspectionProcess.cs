using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting.Processes;

internal static class C1OutcomeInspectionProcess
{
    private const int MaximumInputBytes = 8193;

    internal static Task<C1OutcomeInspectionProcessResult> RunAsync(
        ReadOnlyMemory<byte> input, string outerOwnerLockPath, CancellationToken cancellationToken = default)
        => RunOwnedAsync(input, outerOwnerLockPath, C1OutcomeInspectionProtocol.Mode, cancellationToken);

    internal static Task<C1OutcomeInspectionProcessResult> RunSubmissionOutcomeAsync(
        ReadOnlyMemory<byte> input, string outerOwnerLockPath, CancellationToken cancellationToken)
        => RunOwnedAsync(input, outerOwnerLockPath, C1SubmissionOutcomeInspectionProtocol.Mode, cancellationToken);

    internal static Task<C1OutcomeInspectionProcessResult> RunSubmissionJournalAsync(
        ReadOnlyMemory<byte> input, string outerOwnerLockPath, CancellationToken cancellationToken)
    {
        var request = global::KeyLoad.CrashHost.Features.ClusterReplication.C1SubmissionJournalInspectionJson.ReadRequest(input.Span);
        var owner = global::KeyLoad.CrashHost.Features.ClusterReplication.C1SubmissionJournalInspectionPaths.Owner(request);
        if (!string.Equals(owner, outerOwnerLockPath, StringComparison.Ordinal))
        {
            throw global::KeyLoad.CrashHost.Features.ClusterReplication.C1SubmissionJournalInspectionValidation.Invalid();
        }
        return RunOwnedAsync(input, outerOwnerLockPath,
            global::KeyLoad.CrashHost.Features.ClusterReplication.C1SubmissionJournalInspectionProtocol.Mode, cancellationToken);
    }

    private static async Task<C1OutcomeInspectionProcessResult> RunOwnedAsync(
        ReadOnlyMemory<byte> input, string outerOwnerLockPath, string mode, CancellationToken cancellationToken)
    {
        const int InputLengthEmptyCount = 0;

        ArgumentException.ThrowIfNullOrWhiteSpace(outerOwnerLockPath);
        if (input.Length is InputLengthEmptyCount or > MaximumInputBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }
        cancellationToken.ThrowIfCancellationRequested();
        var failures = new List<Exception>();
        C1OutcomeInspectionProcessResult? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var lifetime = new C1OutcomeInspectionProcessLifetime(input, outerOwnerLockPath, failures, CrashExecutionOptions.Child(), mode);
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
