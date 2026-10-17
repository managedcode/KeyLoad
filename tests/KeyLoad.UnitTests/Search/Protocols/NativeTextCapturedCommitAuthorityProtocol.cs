namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextCapturedCommitAuthorityProtocol
{
    internal const string Collection = "orders";
    internal const string Subject = "captured-commit-reader";
    internal const string First = "first";
    internal const string Second = "second";
    internal const string Third = "third";
    internal const string Field = "/text";
    internal const string Text = "needle";
    internal const string FirstJson = "{\"text\":\"needle\"}";
    internal const string SecondJson = "{\"text\":\"needle needle\"}";
    internal const string UpdatedFirstJson = "{\"text\":\"needle needle needle\"}";
    internal const string PutKind = "putDocument";
    internal const string DeleteKind = "deleteDocument";
    internal const long InitialRevision = 1L;
    internal const long UpdatedRevision = 2L;
    internal const long AbsentRevision = 0L;
    private const int ResultLimit = 3;

    internal static SearchRequest Request(PartitionRef partition)
        => new(partition, Collection, Field, Text, Limit: ResultLimit);

    internal static PrincipalRecord Principal(PartitionRef partition)
        => new(Subject, partition.TenantId,
            [new(partition.DatabaseId, Collection, Capability.Query | Capability.DocumentsRead)], [])
        {
            PolicyEpoch = InitialRevision,
            OwnerId = Subject,
            RestrictRows = true
        };

    internal static CommandRequest Seed(PartitionRef partition, Guid commandId)
        => new(commandId, partition,
        [
            new PutDocument(Collection, First, FirstJson, ExpectedRevision: AbsentRevision,
                Access: new RowAccess(OwnerId: Subject)),
            new PutDocument(Collection, Second, SecondJson, ExpectedRevision: AbsentRevision,
                Access: new RowAccess(OwnerId: Subject))
        ]);

    internal static CommandRequest Mutation(PartitionRef partition, Guid commandId)
        => new(commandId, partition,
        [
            new PutDocument(Collection, First, UpdatedFirstJson, ExpectedRevision: InitialRevision),
            new DeleteDocument(Collection, Second, ExpectedRevision: InitialRevision),
            new PutDocument(Collection, Third, SecondJson, ExpectedRevision: AbsentRevision,
                Access: new RowAccess(OwnerId: Subject))
        ]);
}
