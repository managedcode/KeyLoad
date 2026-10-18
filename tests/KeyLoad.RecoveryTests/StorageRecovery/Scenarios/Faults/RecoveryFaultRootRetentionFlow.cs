using System.Diagnostics;
using System.Text.Json;
using KeyLoad.RecoveryTests.Features.DocumentStorage;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class RecoveryFaultRootRetentionFlow
{
    private const int SavedSeed = 1_701;
    private const int SuccessfulExit = 0;
    private const int NoFailures = 0;
    private const int SingleOriginal = 1;
    private const string Dotnet = "dotnet";
    private const string SidecarSuffix = ".failed-append.json";
    private const string ChildRetentionKey = "RecoveryFaultRootRetentionOriginalChild";
    private const string MissingCompletion = "The original document recovery child did not emit its required boundary signal.";
    private const string UnknownChild = "The failed native append retained its original unsettled child.";

    internal static async Task RunAsync(CancellationToken callerToken)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(
            CommandIdempotencyProcessRecoveryTests.RunTimeoutSeconds), TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(callerToken, deadline.Token);
        var trial = new SeededWalCorruptionTrial(SavedSeed);
        var owner = new SeededWalCorruptionProcessOwner(trial);
        var attempt = new FailedAttempt();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => OwnedAsync(trial, owner, attempt, lifetime.Token), failures);
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(
            CommandIdempotencyProcessRecoveryTests.CleanupTimeoutSeconds), TimeProvider.System);
        if (attempt.Child is { } child)
        {
            await CommandIdempotencyProcess.SettleActiveAsync(child, failures, cleanup.Token);
            if (!child.IsSettled || !child.IsDisposed)
            {
                var retained = new IOException(UnknownChild);
                retained.Data[ChildRetentionKey] = child;
                failures.Add(retained);
            }
        }
        await ServerFailureObserver.ObserveAsync(() => SaveEvidenceAsync(trial, owner, attempt, failures, cleanup.Token), failures);
        await SeededWalCorruptionFlow.CleanupAsync(trial, owner, failures);
        CommandIdempotencyProcessFailureHandling.ThrowFailures(failures);
        await Assert.That(Directory.Exists(trial.Root)).IsFalse();
    }

    private static async Task OwnedAsync(SeededWalCorruptionTrial trial, SeededWalCorruptionProcessOwner owner,
        FailedAttempt attempt, CancellationToken token)
    {
        await owner.PhaseAsync(NativeChecksumProfileProtocol.Seed, token);
        await trial.CaptureAsync(token);
        await trial.DamageAsync(token);
        await owner.PhaseAsync(NativeChecksumProfileProtocol.CorruptionRefusal, token);
        await SeededWalCorruptionFiles.RequireFaultedAsync(trial, token);
        await FailedAppendAsync(trial, attempt, token);
        await RetainBothAsync(trial, attempt.Failure!, token);
        await owner.PhaseAsync(NativeChecksumProfileProtocol.CorruptionRefusal, token);
        await SeededWalCorruptionFiles.RequireFaultedAsync(trial, token);
        await trial.RepairAsync(token);
        await SeededWalCorruptionFiles.RequireRestoredAsync(trial, token);
        foreach (var phase in new[] { NativeChecksumProfileProtocol.FirstAppend,
            NativeChecksumProfileProtocol.SecondAppend, NativeChecksumProfileProtocol.Cold })
        {
            await owner.PhaseAsync(phase, token);
            await SeededWalCorruptionFiles.RequireOriginalAuthorityAsync(trial, token);
        }
        await SeededWalCorruptionFiles.RequireHistoryAsync(owner);
    }

    private static async Task FailedAppendAsync(SeededWalCorruptionTrial trial, FailedAttempt attempt,
        CancellationToken token)
    {
        var child = CommandIdempotencyProcessChild.Create(CommandIdempotencyProcess.OutputLimitCharacters, attempt.Signal);
        attempt.Child = child;
        var failures = new List<Exception>();
        var observations = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() =>
        {
            child.Start(CreateStart(trial.Root));
            child.ThrowStartupFailure();
            attempt.Started = true;
            return Task.CompletedTask;
        }, failures);
        if (attempt.Started)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await child.WaitForAcknowledgementAsync(token);
                attempt.Signaled = true;
            }, observations);
            await ServerFailureObserver.ObserveAsync(() => child.WaitForOriginalExitAndReadersAsync(token), observations);
            attempt.NaturalExitAndReadersJoinedBeforeCleanup = child.IsSettled;
            ServerFailureObserver.Observe(() => attempt.Exit = child.ExitCode, failures);
            attempt.Failure = observations.FirstOrDefault(error => error is InvalidOperationException && error.Message == MissingCompletion);
        }
        foreach (var unexpected in observations.Where(error => !ReferenceEquals(error, attempt.Failure)))
        {
            CommandIdempotencyProcessFailureHandling.AddDistinct(failures, unexpected);
        }
        if (child.IsSettled)
        {
            ServerFailureObserver.Observe(child.CloseNativeProcessAfterJoin, failures);
        }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            await Assert.That(attempt.Started && !attempt.Signaled && attempt.Failure is not null
                && attempt.Exit is not null && attempt.Exit != SuccessfulExit).IsTrue();
            await Assert.That(attempt.NaturalExitAndReadersJoinedBeforeCleanup && child.IsDisposed).IsTrue();
            await RecoveryFileInventory.AssertUnchangedAsync(trial.Root, trial.DamagedFiles, token);
        }, failures);
        CommandIdempotencyProcessFailureHandling.ThrowFailures(failures);
        token.ThrowIfCancellationRequested();
        attempt.Accepted = true;
    }

    private static async Task RetainBothAsync(SeededWalCorruptionTrial trial, Exception original, CancellationToken token)
    {
        var observed = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => CommandIdempotencyProcessCleanup.CleanupActiveTrialAsync(
            trial.Root, null, original), observed);
        await Assert.That(observed.Count).IsEqualTo(SingleOriginal);
        await Assert.That(observed.All(error => ReferenceEquals(error, original))).IsTrue();
        await RecoveryFileInventory.AssertUnchangedAsync(trial.Root, trial.DamagedFiles, token);
        await RecoveryProcessCleanup.SettleAsync(null, trial.Root, trial.Root, original, token);
        await Assert.That(original.Data.Count).IsEqualTo(NoFailures);
        await RecoveryFileInventory.AssertUnchangedAsync(trial.Root, trial.DamagedFiles, token);
    }

    private static async Task SaveEvidenceAsync(SeededWalCorruptionTrial trial, SeededWalCorruptionProcessOwner owner,
        FailedAttempt attempt, List<Exception> failures, CancellationToken token)
    {
        var nativeEvidence = await SeededWalCorruptionEvidence.SaveAsync(trial, owner, failures, token);
        await using var output = new FileStream(nativeEvidence + SidecarSuffix, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous
        });
        await JsonSerializer.SerializeAsync(output, new
        {
            NativeEvidence = nativeEvidence,
            Phase = NativeChecksumProfileProtocol.FirstAppend,
            attempt.Signal,
            attempt.Started,
            attempt.Signaled,
            attempt.Exit,
            attempt.Accepted,
            attempt.NaturalExitAndReadersJoinedBeforeCleanup,
            OriginalExitAndBothReadersJoined = attempt.Child?.IsSettled,
            NativeProcessClosed = attempt.Child?.IsDisposed,
            FailureType = attempt.Failure?.GetType().FullName,
            FailureMessage = attempt.Failure?.Message,
            ProcessKillIsNaturalExitEvidence = false,
            PowerLossQualified = false,
            LinuxQualified = false
        }, cancellationToken: token);
        await output.FlushAsync(token);
    }

    private static ProcessStartInfo CreateStart(string root)
    {
        var start = new ProcessStartInfo(Dotnet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { typeof(CrashScenarioDispatcher).Assembly.Location, CrashFixtureValues.ProcessMode,
            NativeChecksumProfileProtocol.Mode, root, NativeChecksumProfileProtocol.FirstAppend })
        {
            start.ArgumentList.Add(argument);
        }
        return start;
    }

    private sealed class FailedAttempt
    {
        internal string Signal
        {
            get;
        } = NativeChecksumProfileProtocol.CompletionSignalPrefix + NativeChecksumProfileProtocol.FirstAppend;
        internal CommandIdempotencyProcessChild? Child
        {
            get; set;
        }
        internal Exception? Failure
        {
            get; set;
        }
        internal bool Started
        {
            get; set;
        }
        internal bool Signaled
        {
            get; set;
        }
        internal int? Exit
        {
            get; set;
        }
        internal bool NaturalExitAndReadersJoinedBeforeCleanup
        {
            get; set;
        }
        internal bool Accepted
        {
            get; set;
        }
    }
}
