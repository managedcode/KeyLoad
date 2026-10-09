using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class NativeInstallFrameInspection
{
    private const int FirstArgument = 0;
    private const int SingleArgument = 1;
    private const int SuccessExit = 0;

    internal static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length == FirstArgument || !string.Equals(args[FirstArgument], NativeInstallFrameInspectionProtocol.Mode, StringComparison.Ordinal))
        { return false; }
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
        var evidence = new C1OutcomeInspectionFailureEvidence();
        try
        {
            evidence.SetPhase(C1OutcomeInspectionFailurePhase.ValidateRequest);
            if (args.Length != SingleArgument) { throw new InvalidDataException(C1OutcomeInspectionProtocol.InvalidRequest); }
            evidence.SetPhase(C1OutcomeInspectionFailurePhase.ReadInput);
            var bytes = await C1OutcomeInspection.ReadRequestBytesAsync().ConfigureAwait(false);
            evidence.SetPhase(C1OutcomeInspectionFailurePhase.ValidateRequest);
            var request = NativeInstallFrameInspectionJson.ReadRequest(bytes);
            var receipt = NativeInstallFrameInspectionOperation.Run(request, evidence);
            evidence.SetPhase(C1OutcomeInspectionFailurePhase.WriteReceipt);
            await C1OutcomeInspection.WriteBytesAsync(NativeInstallFrameInspectionJson.SerializeReceipt(receipt), false).ConfigureAwait(false);
            Environment.ExitCode = SuccessExit;
        }
        catch (Exception failure) when (!C1OutcomeInspectionFailures.ContainsFatal(failure))
        {
            evidence.Capture(failure);
            var failures = new List<Exception> { failure };
            await ServerFailureObserver.ObserveAsync(() => C1OutcomeInspection.WriteBytesAsync(evidence.Bytes(), true), failures).ConfigureAwait(false);
            if (failures.Any(C1OutcomeInspectionFailures.ContainsFatal)) { ServerFailureObserver.ThrowIfAny(failures); }
            Environment.ExitCode = C1OutcomeInspectionProtocol.FailureExitCode;
            GC.KeepAlive(failures);
        }
        return true;
    }
}
