using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterReplication;

internal static class C1SubmissionJournalInspection
{
    private const int NoArguments = 0;
    private const int ModeArgumentIndex = 0;
    private const int OnlyModeArgumentCount = 1;
    private const int SuccessExitCode = 0;
    internal static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length == NoArguments || !string.Equals(args[ModeArgumentIndex], C1SubmissionJournalInspectionProtocol.Mode, StringComparison.Ordinal))
        {
            return false;
        }
        var evidence = new C1OutcomeInspectionFailureEvidence();
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
        try
        {
            evidence.SetPhase(C1OutcomeInspectionFailurePhase.ValidateRequest);
            if (args.Length != OnlyModeArgumentCount)
            {
                throw C1SubmissionJournalInspectionValidation.Invalid();
            }
            evidence.SetPhase(C1OutcomeInspectionFailurePhase.ReadInput);
            var input = await C1OutcomeInspection.ReadRequestBytesAsync().ConfigureAwait(false);
            evidence.SetPhase(C1OutcomeInspectionFailurePhase.ValidateRequest);
            var request = C1SubmissionJournalInspectionJson.ReadRequest(input);
            var receipt = C1SubmissionJournalInspectionOperation.Run(request, evidence);
            evidence.SetPhase(C1OutcomeInspectionFailurePhase.WriteReceipt);
            await C1OutcomeInspection.WriteBytesAsync(C1SubmissionJournalInspectionJson.SerializeReceipt(receipt),
                standardError: false).ConfigureAwait(false);
            Environment.ExitCode = SuccessExitCode;
        }
        catch (Exception failure) when (!C1OutcomeInspectionFailures.ContainsFatal(failure))
        {
            evidence.Capture(failure);
            var failures = new List<Exception> { failure };
            await ServerFailureObserver.ObserveAsync(() => C1OutcomeInspection.WriteBytesAsync(evidence.Bytes(),
                standardError: true), failures).ConfigureAwait(false);
            if (failures.Any(C1OutcomeInspectionFailures.ContainsFatal))
            {
                ServerFailureObserver.ThrowIfAny(failures);
            }
            Environment.ExitCode = C1OutcomeInspectionProtocol.FailureExitCode;
            GC.KeepAlive(failures);
        }
        return true;
    }
}
