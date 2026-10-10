namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferAttemptColdProtocol
{
    internal const int Ceiling = 2;
    internal const int SingleAttemptCeiling = 1;
    internal const string EmptyReceipt = "";
    internal const long FirstGeneration = 1;
    internal const long SecondGeneration = 2;
    internal const long OneMessage = 1;
    internal const long NoMessages = 0;
    internal const long TwoRetainedRecords = 2;
    internal const int InitialAttempts = 0;
    internal const long InitialStateVersion = 1;
    internal const long SecondReadySequence = 2;
    internal const string OriginalMessage = "attempt-cold-original";
    internal const string FillerMessage = "attempt-cold-filler";
    internal const string Payload = "{\"attempt\":true}";
    internal const string Headers = "{}";
    internal const string Missing = "The actual native attempt state is missing.";
}
