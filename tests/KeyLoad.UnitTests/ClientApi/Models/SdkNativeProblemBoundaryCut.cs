using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed record SdkNativeProblemBoundaryCut(string[] Rows, long Position,
    (long LastIndex, long CommittedIndex, long Term) Journal, byte[] Identity, OutboxSnapshot Outbox, byte[] FailedOutcome);
