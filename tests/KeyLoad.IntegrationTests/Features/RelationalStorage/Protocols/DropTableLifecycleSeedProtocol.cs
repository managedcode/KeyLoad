using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ResourceExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropTableLifecycleSeedProtocol
{
    internal const string PartitionKey = "drop-lifecycle";
    internal const string SdkTable = "d7_lifecycle_sdk";
    internal const string McpTable = "d7_lifecycle_mcp";
    internal const string SqlTable = "d7_lifecycle_sql";
    internal const string AnchorCollection = "d7_lifecycle_anchor";
    internal const string ControlTable = "d7_lifecycle_keep";
    internal const string Graph = "d7_lifecycle_graph";
    internal const string OldLeft = "old-left";
    internal const string OldRight = "old-right";
    internal const string NewLeft = "new-left";
    internal const string NewRight = "new-right";
    internal const string AnchorId = "anchor";
    internal const string ControlId = "control";
    internal const string Id = "id";
    internal const string Title = "title";
    internal const string Amount = "amount";
    internal const string AnchorJson = "{\"anchor\":true}";
    internal const string ControlJson = "{\"id\":\"control\",\"title\":\"keep\",\"amount\":99}";
    internal const string EdgeLabel = "owns";
    internal const string EmptyJson = "{}";
    internal const string PutKind = "putDocument";
    internal const string EdgeKind = "upsertEdge";
    internal const int InitialVersion = 1;
    internal const int First = 0;
    internal const int Second = 1;
    internal const int Third = 2;
    internal const int TableCount = 3;
    internal const int VertexCount = 4;
    internal const int MaxDepth = 1;
    internal const long Absent = 0;
    internal const long Initial = 1;
    internal static PartitionRef Partition => ResourcePartitionInventoryRf3Protocol.Partition(PartitionKey);
    internal static ImmutableArray<string> Names => [SdkTable, McpTable, SqlTable];
    internal static ImmutableArray<string> Edges => ["to_sdk", "to_mcp", "to_sql"];

    internal static ResourceDefinition Table(string name, PartitionRef partition)
        => new(name, ResourceKind.Collection, partition.TransactionDomainId)
        {
            Authority = DocumentAuthority.Document,
            RelationalSchema = new(Id, [new(Id, RelationalColumnType.Text), new(Title, RelationalColumnType.Text),
                new(Amount, RelationalColumnType.WholeNumber)])
        };

    internal static string Create(string name)
        => "CREATE TABLE [" + name + "] (id NVARCHAR(MAX) NOT NULL PRIMARY KEY,title NVARCHAR(MAX) NOT NULL,amount BIGINT NOT NULL)";

    internal static ImmutableArray<DocumentResult> Rows(PartitionRef partition, int table)
    {
        var name = Names[table];
        var json = table switch
        {
            First => new[] { "{\"id\":\"old-left\",\"title\":\"sdk-left\",\"amount\":10}", "{\"id\":\"old-right\",\"title\":\"sdk-right\",\"amount\":11}" },
            Second => new[] { "{\"id\":\"old-left\",\"title\":\"mcp-left\",\"amount\":20}", "{\"id\":\"old-right\",\"title\":\"mcp-right\",\"amount\":21}" },
            Third => new[] { "{\"id\":\"old-left\",\"title\":\"sql-left\",\"amount\":30}", "{\"id\":\"old-right\",\"title\":\"sql-right\",\"amount\":31}" },
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };
        return [new(new(partition, name, OldLeft), Initial, json[First], false, []),
            new(new(partition, name, OldRight), Initial, json[Second], false, [])];
    }
}
