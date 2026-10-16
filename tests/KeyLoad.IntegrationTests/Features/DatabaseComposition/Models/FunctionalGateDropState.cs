using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record FunctionalGateDropState(CommandRequest Original, CommitReceipt Receipt,
    ImmutableArray<FunctionalGateDropTable> Tables);

internal sealed record FunctionalGateDropTable(int Index, EntityRef OriginalReference,
    Guid ParentId, DropTableRequest Request, DropTableResult OriginalDrop,
    DocumentResult Fresh, CommandRequest FreshCommand, CommitReceipt FreshReceipt);
