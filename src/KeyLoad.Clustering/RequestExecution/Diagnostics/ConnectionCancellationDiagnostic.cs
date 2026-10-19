using Microsoft.Extensions.Logging;

namespace KeyLoad.Clustering;

internal static class ConnectionCancellationDiagnostic
{
    private const int OriginalCancellationEvent = 3;
    private const string Message = "Execution connection original cancellation: {RequestId}; CallSiteStage={CallSiteStage}; RequestActive={RequestActive}; AdmissionClosed={AdmissionClosed}; OwnerCancellationRequested={OwnerCancellationRequested}; OriginalTokenMatchesWriter={OriginalTokenMatchesWriter}.";
    private static readonly Action<ILogger, Guid, GrainFailureStage, bool, bool, bool, bool, Exception?> LogOriginal =
        LoggerMessage.Define<Guid, GrainFailureStage, bool, bool, bool, bool>(LogLevel.Warning,
            new EventId(OriginalCancellationEvent), Message);

    internal static void Observe(ILogger logger, OperationCanceledException original, Guid requestId,
        GrainFailureStage callSiteStage, bool requestActive, bool admissionClosed, bool ownerCancelled, bool tokenMatches)
    {
        CapabilityWorkLifetime.Settle(original, null, () =>
            LogOriginal(logger, requestId, Enum.IsDefined(callSiteStage) ? callSiteStage : GrainFailureStage.EnvelopeVerification,
                requestActive, admissionClosed, ownerCancelled, tokenMatches, null), null);
    }
}
