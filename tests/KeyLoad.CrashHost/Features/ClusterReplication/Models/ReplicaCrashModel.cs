using System.Text;

namespace KeyLoad.CrashHost;

/// <summary>Deterministic real database commands shared by the child and its recovery assertions.</summary>
internal static class ReplicaCrashModel
{
    private const int SnapshotDocumentCut = 4;
    private const int TailDocumentCut = 5;

    private const int BaseDocumentCut = 2;
    private const int CommittedDocumentCut = 3;

    private const string Tenant = "replica-crash-tenant";
    private const string Database = "replica-crash-database";
    private const string Domain = "orders";
    private const string PartitionKey = "partition";
    private const string Collection = "orders";
    private const string DocumentId = "document";
    private const string Principal = "root";
    private const string BaseJson = "{\"value\":\"base\"}";
    private const string CommittedJson = "{\"value\":\"committed\"}";
    private const string SnapshotJson = "{\"value\":\"snapshot\"}";
    private const string TailJson = "{\"value\":\"tail\"}";

    /// <summary>The document whose revision and payload expose the canonical applied cut.</summary>
    public static EntityRef Document { get; } = new(new(Tenant, Database, Domain, PartitionKey), Collection, DocumentId);

    /// <summary>The authenticated fixture principal persisted in every real database.</summary>
    public static string PrincipalId => Principal;

    /// <summary>Creates the same operation fingerprint in the child and every reopen.</summary>
    /// <param name="index">The original deterministic operation index, from one through five.</param>
    public static ReplicatedOperation Operation(int index)
    {
        const int FirstOperationIndex = 1;
        const int LastOperationIndex = 5;
        const int GuidClockSequence = 0;
        const int GuidVersionSequence = 0;
        const int GuidTailBytes = 8;
        const int ConfigureResourceOperation = 1;
        const int DocumentOperationOffset = 2;

        ArgumentOutOfRangeException.ThrowIfLessThan(index, FirstOperationIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, LastOperationIndex);
        var id = new Guid(index, GuidClockSequence, GuidVersionSequence, new byte[GuidTailBytes]);
        var timestamp = DateTimeOffset.UnixEpoch.AddTicks(index);
        if (index == ConfigureResourceOperation)
        {
            var resource = new ConfigureResourceRequest(Tenant, Database, new(Collection, ResourceKind.Collection, Domain));
            return new(id, OperationKind.ConfigureResource, Principal, timestamp, Encode(resource));
        }
        var request = new CommandRequest(id, Document.Partition, [new PutDocument(Collection, DocumentId, JsonAt(index), index - DocumentOperationOffset)]);
        return new(id, OperationKind.Batch, Principal, timestamp, Encode(request));
    }

    /// <summary>The caller-visible document at a materialized replication cut.</summary>
    /// <param name="index">The committed document operation whose exact JSON is required.</param>
    public static string JsonAt(long index) => index switch
    {
        BaseDocumentCut => BaseJson,
        CommittedDocumentCut => CommittedJson,
        SnapshotDocumentCut => SnapshotJson,
        TailDocumentCut => TailJson,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    private static string Encode<T>(T value) => Encoding.UTF8.GetString(JsonDefaults.Serialize(value));
}
