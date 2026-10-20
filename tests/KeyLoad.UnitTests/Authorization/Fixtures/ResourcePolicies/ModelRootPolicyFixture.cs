using KeyLoad.Query;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class ModelRootPolicyFixture(bool queue, bool headerRoot) : IDisposable
{
    internal const string Source = "root-source";
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
    internal const string Target = "root-target";
    internal const string TargetId = "c1";
    internal const string TargetJson = "{\"id\":\"c1\",\"name\":\"Ada\"}";
    internal const string StreamId = "stream";
    internal const string Reader = "root-policy-reader";
    internal const string RootRead = "root.read";
    internal const string RootUse = "root.use";
    internal const string Headers = "{\"\":\"HEADER-EMPTY\",\"public\":\"header-public\",\"secret\":\"HEADER-VALUE\"}";
    internal const string SafeHeaders = "{\"public\":\"header-public\",\"secret\":\"HEADER-VALUE\"}";
    internal static readonly DateTimeOffset Epoch = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
    internal TestDatabase Harness { get; } = new(timeProvider: new ControlledReadClock(Epoch));
    internal bool Queue => queue;
    internal bool HeaderRoot => headerRoot;
    internal string Id => queue ? "message" : "event";
    internal string RootComponent => headerRoot ? "headers" : "payload";
    internal string RootValue => headerRoot ? "HEADER-VALUE" : "PAYLOAD-VALUE";
    internal ModelQuerySource Model => new(queue ? ModelQuerySourceKind.QueueMessages : ModelQuerySourceKind.Events,
        queue ? Source : StreamId, 1L);
    internal string ModelOrder => queue ? "/id" : "/revision";
    internal string AccessPath => queue ? "model-scan:queue-messages" : "model-scan:events";
    internal CommandRequest Seed { get; private set; } = null!;
    internal CommitReceipt Receipt { get; private set; } = null!;
    internal byte[] Outcome { get; private set; } = null!;
    internal string Payload => "{\"\":\"PAYLOAD-EMPTY\",\"public\":\"payload-public\",\"secret\":\"PAYLOAD-VALUE\",\"to\":"
        + System.Text.Json.JsonSerializer.Serialize(new EntityRef(Harness.Partition, Target, TargetId), JsonDefaults.Options) + "}";
    internal string SafePayload => "{\"public\":\"payload-public\",\"secret\":\"PAYLOAD-VALUE\",\"to\":"
        + System.Text.Json.JsonSerializer.Serialize(new EntityRef(Harness.Partition, Target, TargetId), JsonDefaults.Options) + "}";

    internal async Task InitializeAsync()
    {
        var resource = new ResourceDefinition(Source, queue ? ResourceKind.WorkQueue : ResourceKind.StreamSet,
            Harness.Partition.TransactionDomainId)
        {
            FieldPolicies = [Policy(headerRoot ? "/" : "", !headerRoot)],
            HeaderPolicies = [Policy(headerRoot ? "" : "/", headerRoot)]
        };
        var actual = Harness.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(
            Harness.Partition.TenantId, Harness.Partition.DatabaseId, resource)).Get<ResourceDefinition>();
        await ModelRootPolicyAssertions.EqualAsync(actual, resource);
        await ModelRootPolicyAssertions.EqualAsync(Harness.Store.Read(view => view.GetRecord<ResourceDefinition>(
            KeySpace.Resource(Harness.Partition.TenantId, Harness.Partition.DatabaseId, Source))), resource);
        var target = new ResourceDefinition(Target, ResourceKind.Collection, Harness.Partition.TransactionDomainId)
        {
            RelationalSchema = new(IdField, [new(IdField, RelationalColumnType.Text), new("name", RelationalColumnType.Text)])
        };
        Harness.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(Harness.Partition.TenantId,
            Harness.Partition.DatabaseId, target)).Get<ResourceDefinition>();
        Mutation source = queue ? new EnqueueMessage(Source, Id, Payload, Headers)
            : new AppendEvents(Source, StreamId, [new(Id, "Created", Payload, Headers)], ExpectedStreamRevision.NoStream);
        Seed = new(Guid.NewGuid(), Harness.Partition, [new PutDocument(Target, TargetId, TargetJson), source]);
        var before = Harness.Store.Position;
        var expectedToken = Harness.NextCommitToken();
        Receipt = Harness.Submit(OperationKind.Batch, Seed, id: Seed.CommandId).Get<CommitReceipt>();
        var expected = new CommitReceipt(Seed.CommandId, expectedToken, [new("putDocument", Target, TargetId, 1L), new(queue ? "enqueue" : "appendEvents", Source, queue ? Id : StreamId, 1L)], DurabilityProfile.ProcessDurable, 1);
        await ModelRootPolicyAssertions.EqualAsync(Receipt, expected);
        foreach (var mutation in Receipt.Mutations)
        {
            await Assert.That(mutation.CompositionReferences).IsEmpty();
        }
        await Assert.That(Harness.Store.Position).IsEqualTo(before + 1L);
        Outcome = Harness.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(Harness.Partition, "root", Seed.CommandId)))
            ?? throw new InvalidOperationException("The original model source outcome must exist.");
        await ConfigureAsync(1L, false, false);
    }

    private static SensitiveFieldPolicy Policy(string path, bool root)
        => new(path, root ? "whole-source" : "literal-empty-child", root ? RootRead : "literal.read", root ? RootUse : "literal.use");

    internal async Task ConfigureAsync(long epoch, bool use, bool read, bool revoked = false)
    {
        var expected = new PrincipalRecord(Reader, Harness.Partition.TenantId,
            [new(Harness.Partition.DatabaseId, Source, Capability.Query | (queue ? Capability.QueueInspect : Capability.EventsRead)),
             new(Harness.Partition.DatabaseId, Target, Capability.Query | Capability.DocumentsRead)],
            [.. (use ? new[] { RootUse } : Array.Empty<string>()), .. (read ? new[] { RootRead } : Array.Empty<string>())])
        {
            PolicyEpoch = epoch,
            Revoked = revoked
        };
        var actual = Harness.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(expected)).Get<PrincipalRecord>();
        await ModelRootPolicyAssertions.EqualAsync(actual, expected);
        await ModelRootPolicyAssertions.EqualAsync(Harness.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(Reader))), expected);
    }

    public void Dispose() => Harness.Dispose();
}
