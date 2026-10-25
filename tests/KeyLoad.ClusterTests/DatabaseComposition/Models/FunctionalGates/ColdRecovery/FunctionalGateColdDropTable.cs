namespace KeyLoad.ClusterTests.DatabaseComposition;

internal sealed record FunctionalGateColdDropTable(int Index, EntityRef OriginalReference,
    Guid ParentId, DropTableRequest Request, DropTableResult OriginalDrop, CommandRequest OriginalInsert,
    DocumentResult Fresh, CommandRequest FreshCommand, CommitReceipt FreshReceipt,
    SqlOperationRequest AdministratorInsert, CommandRequest AdministratorCommand, CommitReceipt AdministratorReceipt);
