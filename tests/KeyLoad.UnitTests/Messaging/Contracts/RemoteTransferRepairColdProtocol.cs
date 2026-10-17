namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferRepairColdProtocol
{
    internal const string Subject = "queue-transfer-repair-worker";
    internal const string Wildcard = "*";
    internal const string Message = "repair-original";
    internal const string Payload = "{\"repair\":true}";
    internal const string Headers = "{}";
    internal const string MalformedWitnessSuffix = ".invalid";
    internal const string Missing = "The actual native transfer repair state is missing.";
    internal const int FullCeiling = 3;
    internal const int ExhaustedCeiling = 2;
    internal const long InitialEpoch = 1;
    internal const long FirstGeneration = 1;
    internal const long RepairedGeneration = 2;
    internal const long InitialRecords = 1;
    internal const long FullRepairRecords = 3;
    internal const long LimitedRepairRecords = 2;
    internal const int NoAttempts = 0;
    internal const int ClaimedAttempts = 1;
    internal const long AckedStateVersion = 3;
    internal const long OriginalLeaseVersion = 1;
    internal const long InitialStateVersion = 1;
    internal const long InitialReadySequence = 1;
}
