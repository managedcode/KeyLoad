using KeyLoad.UnitTests.DocumentStorage;

namespace KeyLoad.UnitTests.ClientApi;

internal sealed record SdkProblemBoundaryCut(string[] Rows, long Position,
    (long AppliedPosition, long StorePosition) Cut, byte[] Identity, OutboxSnapshot Outbox);
