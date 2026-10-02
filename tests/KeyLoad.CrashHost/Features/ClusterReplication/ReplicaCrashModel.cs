using System.Text;

namespace KeyLoad.CrashHost;

/// <summary>Deterministic real database commands shared by the child and its recovery assertions.</summary>
internal static class ReplicaCrashModel
{
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
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, 5);
        var id = new Guid(index, 0, 0, new byte[8]);
        var timestamp = DateTimeOffset.UnixEpoch.AddTicks(index);
        if (index == 1)
        {
            var resource = new ConfigureResourceRequest(Tenant, Database, new(Collection, ResourceKind.Collection, Domain));
            return new(id, OperationKind.ConfigureResource, Principal, timestamp, Encode(resource));
        }
        var request = new CommandRequest(id, Document.Partition, [new PutDocument(Collection, DocumentId, JsonAt(index), index - 2)]);
        return new(id, OperationKind.Batch, Principal, timestamp, Encode(request));
    }

    /// <summary>The caller-visible document at a materialized replication cut.</summary>
    /// <param name="index">The committed document operation whose exact JSON is required.</param>
    public static string JsonAt(long index) => index switch
    {
        2 => BaseJson,
        3 => CommittedJson,
        4 => SnapshotJson,
        5 => TailJson,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    private static string Encode<T>(T value) => Encoding.UTF8.GetString(JsonDefaults.Serialize(value));
}
