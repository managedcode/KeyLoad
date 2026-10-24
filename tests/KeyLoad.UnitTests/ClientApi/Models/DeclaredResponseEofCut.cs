using KeyLoad.UnitTests.DocumentStorage;

namespace KeyLoad.UnitTests.ClientApi;

internal sealed record DeclaredResponseEofCut(string[] Rows, long PhysicalPosition,
    (long AppliedPosition, long StorePosition) Cut, OutboxSnapshot Outbox, byte[] Outcome);
