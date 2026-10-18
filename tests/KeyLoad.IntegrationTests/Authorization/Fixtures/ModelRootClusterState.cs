using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed record ModelRootClusterState(PartitionRef Partition, bool Queue, bool HeaderRoot, string Resource,
    string Reader, string Key, CommandRequest Seed, CommitReceipt Receipt, QueryRow Original)
{
    internal const string Body = "{\"\":\"EMPTY-CHILD\",\"public\":\"PUBLIC-VALUE\",\"secret\":\"ROOT-CANARY\"}";
    internal const string ReadGrant = "model-root.read";
    internal const string UseGrant = "model-root.use";
    internal const string Item = "root-item";
    internal const string Type = "Created";
    internal const string PayloadField = "payload";
    internal const string HeadersField = "headers";
    internal const string IdField = "id";
    internal const string EventIdField = "eventId";
    internal const string StateField = "state";
    internal const string AttemptsField = "attempts";
    internal const string StateVersionField = "stateVersion";
    internal const string NotBeforeField = "notBefore";
    internal const string ExpiresAtField = "expiresAt";
    internal const string EventTypeField = "eventType";
    internal const string SchemaVersionField = "schemaVersion";
    internal const string RevisionField = "revision";
    internal const string EventSequenceField = "eventSequence";
    internal const string RecordedAtField = "recordedAt";
    internal const string SecretField = "secret";
    internal const int Limit = 10;
    internal string Component => HeaderRoot ? HeadersField : PayloadField;
    internal string Order => Queue ? "/id" : "/revision";
    internal string AccessPath => Queue ? "model-scan:queue-messages" : "model-scan:events";
    internal AstQueryRequest Ast(bool filter = false, bool explicitValue = false)
        => new(Partition, new(Resource, null,
            explicitValue ? [new("/" + Component + "/secret", "secret")] : [new("*", "*")],
            filter ? new Comparison(new FieldOperand("/" + Component + "/secret"), "=", ValueOperand.Create("ROOT-CANARY")) : null,
            [new(Order, false)], Limit,
            ModelSource: new(Queue ? ModelQuerySourceKind.QueueMessages : ModelQuerySourceKind.Events, Queue ? Resource : Item, 1L)),
            AllowFullScan: true);
    internal QueryRequest Sql(bool filter = false, bool explicitValue = false)
        => new(Partition, "SELECT TOP (10) " + (explicitValue ? Component + ".secret AS secret" : "*") + " FROM "
            + (Queue ? "QUEUE_MESSAGES('" + Resource + "')" : "EVENTS('" + Resource + "','" + Item + "')")
            + (filter ? " WHERE " + Component + ".secret = 'ROOT-CANARY'" : "")
            + " ORDER BY " + (Queue ? "id" : "revision"), AllowFullScan: true);
    internal PrincipalRecord Principal(long epoch, bool use, bool read)
        => new(Reader, Partition.TenantId,
            [new(Partition.DatabaseId, Resource, Capability.Query | (Queue ? Capability.QueueInspect : Capability.EventsRead))],
            [.. (use ? new[] { UseGrant } : Array.Empty<string>()), .. (read ? new[] { ReadGrant } : Array.Empty<string>())])
        {
            PolicyEpoch = epoch
        };
}
