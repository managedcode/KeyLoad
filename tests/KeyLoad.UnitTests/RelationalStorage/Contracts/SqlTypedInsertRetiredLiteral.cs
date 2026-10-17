namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal static class SqlTypedInsertRetiredLiteral
{
    internal const string Table = "retained-sql-insert";
    internal const string Writer = "retained-insert-writer";
    internal const string One = "retained-row";
    internal const string Two = "target-row";
    internal const string Id = "id";
    internal const string Title = "title";
    internal const string TitlePath = "/title";
    internal const string IdPath = "/@id";
    internal const string ReadGrant = "retained.read";
    internal const string UseGrant = "retained.use";
    internal const string WriteGrant = "retained.write";
    internal const string OneJson = "{\"id\":\"retained-row\",\"title\":\"before-move\"}";
    internal const string TwoJson = "{\"id\":\"target-row\",\"title\":\"after-repair\"}";
    internal const string InsertSql = "INSERT INTO [retained-sql-insert] (id,title) VALUES (N'retained-row',N'before-move')";
    internal const string ReadSql = "SELECT TOP (8) title FROM [retained-sql-insert] ORDER BY id";
    internal const string Existing = "The inserted canonical row already exists.";
    internal const string Classification = "private";
    internal const string TitleIndex = "retained-title";
    internal const string PutKind = "putDocument";
    internal const long Revision = 1;
    internal const long OriginalEpoch = 1;
    internal const long PublishedEpoch = 2;
    internal const long SourceSeedEntries = 4;
    internal const long TargetSeedEntries = 1;
    internal const int MaximumRows = 8;
    internal const int Version = 1;
    internal static readonly Guid OriginalId = Guid.Parse("535b4a50-0317-4ef5-bf68-61c4843a8d7e");
    internal static readonly Guid FailedId = Guid.Parse("cda2d806-56fa-43eb-bbcd-e27830312494");
    internal static readonly Guid HealthyId = Guid.Parse("a5c7a21a-a355-4bca-92ad-fd9b4e7c42c4");
    internal static ResourceDefinition Definition(PartitionRef partition) => new(Table, ResourceKind.Collection, partition.TransactionDomainId)
    {
        RelationalSchema = new(Id, [new(Id, RelationalColumnType.Text), new(Title, RelationalColumnType.Text)]),
        FieldPolicies = [new(TitlePath, Classification, ReadGrant, UseGrant, WriteGrant)],
        Indexes = [new(TitleIndex, [TitlePath], Unique: true)]
    };
    internal static PrincipalRecord Principal(PartitionRef partition) => new(Writer, partition.TenantId,
        [new(partition.DatabaseId, Table, Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query)],
        [ReadGrant, UseGrant, WriteGrant]);
}
