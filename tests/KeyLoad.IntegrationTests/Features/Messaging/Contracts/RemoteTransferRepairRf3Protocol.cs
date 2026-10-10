namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferRepairRf3Protocol
{
    internal const int Ceiling = 2;
    internal const long InitialGeneration = 1;
    internal const long RepairedGeneration = 2;
    internal const long ReadySequence = 1;
    internal const int ClaimedAttempts = 1;
    internal const long AckedVersion = 3;
    internal const long OriginalLeaseVersion = 1;
    internal const string Missing = "The actual policy-repaired RF3 transfer result is unavailable.";
}
