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
    WalFullLogCorruption,
    NativeDatabaseMissing,
    Serialization,
    TypeInitialization,
    Invocation,
    MissingKey,
    InvalidCast,
    NotSupported,
    Cancelled,
    NullReference,
    Range,
    Overflow,
    Format,
    Timeout,
    AssemblyLoad,
    NativeLibraryMissing,
    NativeEntryPointMissing,
    TypeLoad,
    MissingMethod,
    MissingField,
    OptionsInvalid
}

internal sealed record C1OutcomeInspectionFailureRecord(
    C1OutcomeInspectionFailurePhase Phase, C1OutcomeInspectionFailureKind Kind, int? Code);
