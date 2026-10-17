namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed record C1OutcomeInspectionFailureRecord(
    C1OutcomeInspectionFailurePhase Phase, C1OutcomeInspectionFailureKind Kind, int? Code);
