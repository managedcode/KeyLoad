namespace KeyLoad.ClusterTests.DatabaseComposition;

internal sealed record FunctionalGateDropTable(int Index, EntityRef OriginalReference,
    Guid ParentId, DropTableRequest Request, DropTableResult OriginalDrop,
    DocumentResult Fresh, CommandRequest FreshCommand, CommitReceipt FreshReceipt);
