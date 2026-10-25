namespace KeyLoad.ProcessRecoveryTests.Messaging;

internal static class ExternalQueueWorkerCrashProtocol
{
    internal const string Mode = "external-queue-worker";
    internal const string Marker = "WORKER_LEASE:";
    internal const string InvalidInput = "The private external worker input exceeds its bounded contract.";
    internal const int LeasedVersion = 2;
    internal const string Message = "first";
    internal const string Payload = "{\"job\":1}";
    internal const string Headers = "{\"origin\":\"worker\"}";
    internal const int Arguments = 1;
    internal const int ModeArgumentIndex = 0;
    internal const int BufferStart = 0;
    internal const int EndOfInput = 0;
    internal const int FrameOverflowMargin = 1;
    internal const int HandlerLimit = 1;
    internal const int DeliveryLimit = 1;
    internal const int ClaimAttempt = 1;
    internal const int ReadySequence = 1;
    internal const int LeaseVersion = 1;
    internal const int DeliveryGeneration = 1;
    internal const int MaximumFrameCharacters = 65_536;
    internal const int LeaseSeconds = 300;
    internal const int RenewalSeconds = 299;
    internal const string InputAlias = "keyload.tests.external-worker.input";
    internal const string ClaimAlias = "keyload.tests.external-worker.claim";
}
