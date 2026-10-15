using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed record DeclaredResponseEofCut(string[] Rows, long PhysicalPosition,
    (long LastIndex, long CommittedIndex, long Term) Journal, OutboxSnapshot Outbox, byte[] Outcome);
