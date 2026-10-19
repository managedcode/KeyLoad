namespace KeyLoad.Core.Features.Messaging;

internal static class QueueRetryProtocol
{
    internal const string EnvelopeAlias = "keyload.core.queue-retry-decisions";
    internal const string DecisionAlias = "keyload.core.queue-retry-decision";
    internal const string SeedAlias = "keyload.core.queue-retry-seed";
    internal const int Initial = 0;
    internal const int Positive = 1;
    internal const int Version = 1;
    internal const int Field0 = 0;
    internal const int Field1 = 1;
    internal const int Field2 = 2;
    internal const int Field3 = 3;
    internal const int Field4 = 4;
    internal const int Field5 = 5;
    internal const int Field6 = 6;
    internal const int Field7 = 7;
    internal const int Field8 = 8;
    internal const int Field9 = 9;
    internal const int DigestBytes = 32;
    internal const string InvalidDecision = "The recorded queue retry decision is invalid.";
    internal const string MissingDecision = "The recorded queue retry decision is unavailable.";
    internal const string StaleDecision = "The recorded queue retry decision no longer matches its input.";
}
