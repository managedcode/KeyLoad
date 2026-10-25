using KeyLoad.FunctionalTests.DocumentStorage;

namespace KeyLoad.FunctionalTests.ClientApi;

internal sealed record DeclaredResponseEofCut(string[] Rows, long PhysicalPosition,
    (long AppliedPosition, long StorePosition) Cut, OutboxSnapshot Outbox, byte[] Outcome);
