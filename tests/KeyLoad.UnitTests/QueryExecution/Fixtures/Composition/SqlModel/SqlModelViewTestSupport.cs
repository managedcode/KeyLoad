namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlModelViewTestSupport
{
    internal const string StreamSet = "events";
    internal const string StreamId = "stream-a";
    internal const string Queue = "jobs";
    internal const string EventId = "event-a";
    internal const string MessageId = "message-a";
    internal const string Secret = "MODEL_VIEW_PRIVATE_CANARY";
    internal const string HeaderSecret = "MODEL_VIEW_HEADER_CANARY";
    internal const string Reader = "model-reader";
    internal const string EventTypeField = "eventType";
    internal const string PayloadField = "payload";
    internal const string VisibleField = "visible";
    internal const string QueueStateField = "state";
    internal const string AttemptsField = "attempts";
    internal const string PartitionKey = "partition";
    internal const string ModelSourceProperty = "modelSource";

    internal static TestDatabase Create(DatabaseLimits? limits = null)
    {
        var database = new TestDatabase(limits);
        Configure(database, StreamSet, ResourceKind.StreamSet, headerPolicies: [new("/privateHeader", "private",
            "header.read", "header.use")]);
        Configure(database, Queue, ResourceKind.WorkQueue, headerPolicies: [new("/privateHeader", "private",
            "header.read", "header.use")]);
        return database;
    }

    internal static void Configure(TestDatabase database, string name, ResourceKind kind,
        SensitiveFieldPolicy[]? fields = null, SensitiveFieldPolicy[]? headerPolicies = null,
        QueuePolicy? queuePolicy = null)
    {
        var resource = new ResourceDefinition(name, kind, database.Partition.TransactionDomainId)
        {
            FieldPolicies = [.. fields ?? [new("/secret", "private", "secret.read", "secret.use")]],
            HeaderPolicies = [.. headerPolicies ?? []],
            QueuePolicy = queuePolicy ?? new()
        };
        database.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, resource))
            .Get<ResourceDefinition>();
    }

    internal static void Seed(TestDatabase database)
    {
        database.Commit(
            new AppendEvents(StreamSet, StreamId,
                [new(EventId, "Created", Payload(Secret, "event-visible"), Headers(HeaderSecret))],
                ExpectedStreamRevision.NoStream),
            new EnqueueMessage(Queue, MessageId, Payload(Secret, "queue-visible"), Headers(HeaderSecret)));
    }

    internal static void Principal(TestDatabase database, string id, Capability eventCapabilities,
        Capability queueCapabilities, string[]? fieldGrants = null)
        => database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(id,
            database.Partition.TenantId,
            [new(database.Partition.DatabaseId, StreamSet, eventCapabilities),
             new(database.Partition.DatabaseId, Queue, queueCapabilities)],
            System.Collections.Immutable.ImmutableArray.CreateRange(fieldGrants ?? []))))
            .Get<PrincipalRecord>();

    internal static QueryRequest Request(TestDatabase database, string sql, bool allowFullScan = true,
        string? cursor = null) => new(database.Partition, sql, AllowFullScan: allowFullScan, Cursor: cursor);

    internal static string Payload(string secret, string visible)
        => "{\"secret\":\"" + secret + "\",\"visible\":\"" + visible + "\"}";

    internal static string Headers(string secret)
        => "{\"privateHeader\":\"" + secret + "\",\"trace\":\"trace-visible\"}";
}
