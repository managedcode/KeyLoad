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

internal enum C1OutcomeInspectionFailureKind
{
    KeyLoad,
    InvalidData,
    MissingFile,
    MissingDirectory,
    Unauthorized,
    Io,
    Argument,
    InvalidOperation,
    Json,
    Other,
    WalCorruption,
    WalFullLogCorruption
}

internal sealed record C1OutcomeInspectionFailureRecord(
    C1OutcomeInspectionFailurePhase Phase, C1OutcomeInspectionFailureKind Kind, int? Code);
