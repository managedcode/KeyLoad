using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed record DeclaredResponseEofCut(string[] Rows, long PhysicalPosition,
    (long AppliedPosition, long StorePosition) Cut, OutboxSnapshot Outbox, byte[] Outcome);
