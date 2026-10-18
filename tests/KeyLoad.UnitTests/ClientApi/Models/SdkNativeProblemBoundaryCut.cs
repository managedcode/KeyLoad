using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed record SdkNativeProblemBoundaryCut(string[] Rows, long Position,
    (long AppliedPosition, long StorePosition) Cut, byte[] Identity, OutboxSnapshot Outbox);
