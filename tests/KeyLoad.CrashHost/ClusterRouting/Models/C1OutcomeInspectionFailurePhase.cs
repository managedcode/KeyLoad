namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal enum C1OutcomeInspectionFailurePhase
{
    ReadInput,
    ValidateRequest,
    OpenStore,
    ReadOutcome,
    DisposeStore,
    WriteReceipt
}
