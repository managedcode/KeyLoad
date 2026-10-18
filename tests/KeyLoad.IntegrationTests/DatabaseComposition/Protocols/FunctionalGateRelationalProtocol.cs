namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateRelationalProtocol
{
    internal const string Parent = "gate_sql_0";
    internal const string Child = "gate_sql_1";
    internal const string Independent = "gate_sql_2";
    internal const string Baseline = "baseline";
    internal const string Id = "id";
    internal const string Title = "title";
    internal const string Amount = "amount";
    internal const string DocumentId = "doc_id";
    internal const string AnchorReference = "anchor_ref";
    internal const string ParentId = "parent_id";
    internal const string Label = "label";
    internal const string Active = "active";
    internal const string AmountPath = "/amount";
    internal const string ParentPath = "/parent_id";
    internal const string ActivePath = "/active";
    internal const string PutKind = "putDocument";
    internal const string PatchKindName = "patchDocument";
    internal const string DeleteKind = "deleteDocument";
    internal const string ParentSelection = "SELECT TOP (2) title,amount FROM [gate_sql_0] WHERE id = @id";
    internal const string ChildSelection = "SELECT TOP (2) parent_id,label FROM [gate_sql_1] WHERE id = @id";
    internal const string IndependentSelection = "SELECT TOP (2) active FROM [gate_sql_2] WHERE id = @id";
    internal const string Join = "SELECT TOP (2) c.id AS child_id,p.title AS parent_title,p.amount AS amount "
        + "FROM [gate_sql_1] c INNER JOIN [gate_sql_0] p ON c.parent_id = p.id WHERE c.id = @id ORDER BY c.id";
    internal const string Insert = "INSERT INTO [gate_sql_0] (id,title,amount,doc_id,anchor_ref) "
        + "VALUES (@id,N'transient',10,@doc_id,@anchor_ref)";
    internal const string ParentBefore = "{\"title\":\"transient\",\"amount\":10}";
    internal const string ParentAfter = "{\"title\":\"transient\",\"amount\":11}";
    internal const string BaselineParent = "{\"title\":\"parent\",\"amount\":10}";
    internal const string BaselineChild = "{\"parent_id\":\"baseline\",\"label\":\"child\"}";
    internal const string TrueRow = "{\"active\":true}";
    internal const string FalseRow = "{\"active\":false}";
    internal const string ChangedAmount = "11";
    internal const string NullJson = "null";
    internal const string FalseJson = "false";
    internal const string LeftAlias = "c";
    internal const string RightAlias = "p";
    internal const string JoinAccessPath = "bounded-primary-key-inner-join";
    internal const int First = 0;
    internal const int Second = 1;
    internal const int TableCount = 3;
    internal const int Dialect = 2;
    internal const long Absent = 0;
    internal const long Initial = 1;
    internal const long Updated = 2;
    internal const long Unlinked = 3;
    internal const long DeletedChild = 4;
    internal static string[] Tables => [Parent, Child, Independent];
    internal static string[] Create =>
    [
        "CREATE TABLE [gate_sql_0] (id NVARCHAR(MAX) NOT NULL PRIMARY KEY,title NVARCHAR(MAX) NOT NULL,"
            + "amount BIGINT NOT NULL,doc_id NVARCHAR(MAX) NOT NULL,anchor_ref NVARCHAR(MAX) NOT NULL)",
        "CREATE TABLE [gate_sql_1] (id NVARCHAR(MAX) NOT NULL PRIMARY KEY,parent_id NVARCHAR(MAX) NULL,label NVARCHAR(MAX) NOT NULL)",
        "CREATE TABLE [gate_sql_2] (id NVARCHAR(MAX) NOT NULL PRIMARY KEY,active BIT NOT NULL)"
    ];

    internal static ResourceDefinition Definition(PartitionRef partition, int table)
        => new(Tables[table], ResourceKind.Collection, partition.TransactionDomainId)
        {
            Authority = DocumentAuthority.Document,
            RelationalSchema = new(Id, table switch
            {
                First => [new(Id, RelationalColumnType.Text), new(Title, RelationalColumnType.Text),
                    new(Amount, RelationalColumnType.WholeNumber), new(DocumentId, RelationalColumnType.Text),
                    new(AnchorReference, RelationalColumnType.Text)],
                Second => [new(Id, RelationalColumnType.Text), new(ParentId, RelationalColumnType.Text, true),
                    new(Label, RelationalColumnType.Text)],
                _ => [new(Id, RelationalColumnType.Text), new(Active, RelationalColumnType.Boolean)]
            })
        };
}
