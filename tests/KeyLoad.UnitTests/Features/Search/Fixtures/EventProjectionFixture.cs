using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class EventProjectionFixture : IDisposable
{
    internal const string Collection = "projection-records";
    internal const string SeparateTargetCollection = "projection-derived";
    internal const string StreamSet = "projection-events";
    internal const string StreamId = "projection-stream";
    internal const string SourceId = "source-record";
    internal const string TargetId = "derived-record";
    internal const string InputField = "/input";
    internal const string VectorField = "/embedding";
    internal const string SourceClass = "projection-sensitive";
    internal const string InputUse = "projection.input.use";
    internal const string InputWrite = "projection.input.write";
    internal const string VectorUse = "projection.vector.use";
    internal const string VectorWrite = "projection.vector.write";
    internal const string ReaderId = "projection-reader";
    internal const string WorkerId = "projection-worker";
    internal const string Secret = "private source payload";
    internal static VectorSpace Space { get; } = new("projection-space", 2, DistanceMetric.Cosine,
        "projection-model", "projection-v1");

    internal EventProjectionHarness Harness { get; }
    internal DatabaseEngine Database => Harness.Database;
    internal PartitionRef Partition => Harness.Partition;
    internal StreamRef Stream => new(Partition, StreamSet, StreamId, 1);
    internal string TargetCollection { get; }

    internal EventProjectionFixture(string? targetClassification = SourceClass, RowAccess? sourceAccess = null,
        bool separateTarget = false, string? sourceClassification = SourceClass, RowAccess? targetAccess = null)
    {
        TargetCollection = separateTarget ? SeparateTargetCollection : Collection;
        Harness = new EventProjectionHarness();
        Harness.Configure(StreamSet, ResourceKind.StreamSet);
        ConfigureCollection(targetClassification, sourceClassification);
        if (separateTarget)
        {
            Harness.Configure(TargetCollection, ResourceKind.Collection,
                [new(VectorField, targetClassification ?? SourceClass, RawUseGrant: VectorUse, WriteGrant: VectorWrite)]);
        }
        Harness.Commit(
            new PutDocument(Collection, SourceId, "{\"input\":\"" + Secret + "\"}", Access: sourceAccess),
            new PutDocument(TargetCollection, TargetId, "{\"input\":\"derived\"}", Access: targetAccess));
        Harness.Commit(new AppendEvents(StreamSet, StreamId,
            [new EventData("projection-event", "SourceUpdated", "{\"source\":\"" + Secret + "\"}")],
            ExpectedStreamRevision.NoStream));
        ConfigureWorker();
        ConfigureReader([InputUse, VectorUse]);
    }

    internal void ConfigureCollection(string? targetClassification = SourceClass,
        string? sourceClassification = SourceClass)
    {
        var policies = new List<SensitiveFieldPolicy>();
        if (sourceClassification is not null)
        {
            policies.Add(new(InputField, sourceClassification, RawUseGrant: InputUse, WriteGrant: InputWrite));
        }
        policies.Add(new(VectorField, targetClassification ?? SourceClass, RawUseGrant: VectorUse,
            WriteGrant: VectorWrite));
        Harness.Configure(Collection, ResourceKind.Collection, fields: [.. policies]);
    }

    internal void ReclassifySource(string classification)
    {
        var key = KeySpace.Resource(Partition.TenantId, Partition.DatabaseId, Collection);
        var current = Harness.Store.Read(view => view.GetRecord<ResourceDefinition>(key))
            ?? throw new InvalidOperationException("The projection source resource must exist before reclassification.");
        var replacement = current with
        {
            FieldPolicies = [.. current.FieldPolicies.Select(policy => string.Equals(policy.Path, InputField,
                StringComparison.Ordinal) ? policy with { Classification = classification } : policy)],
            SchemaVersion = checked(current.SchemaVersion + 1)
        };
        Harness.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(Partition.TenantId,
            Partition.DatabaseId, replacement)
        { ExpectedSchemaVersion = current.SchemaVersion }).Get<ResourceDefinition>();
    }

    internal void ConfigureWorker(bool revoked = false, string[]? additionalStreams = null, string[]? grants = null)
        => ConfigurePrincipal(WorkerId, Capability.EventsRead | Capability.DocumentsRead | Capability.DocumentsWrite,
            grants ?? [InputUse, InputWrite, VectorUse, VectorWrite], revoked: revoked,
            additionalStreams: additionalStreams);

    internal void ConfigureReader(string[] grants, bool revoked = false, bool restrictRows = false,
        string? owner = null, string[]? projects = null)
        => ConfigurePrincipal(ReaderId, Capability.Query | Capability.DocumentsRead | Capability.VectorSearch,
            grants, revoked, restrictRows, owner, projects: projects);

    internal ApplyVectorProjection Request(ImmutableArray<float>? values = null, long sourceRevision = 1,
        string eventId = "projection-event")
        => new(Stream, sourceRevision, eventId, new(Partition, Collection, SourceId), 1,
            InputField, "projection-reducer", "v1", 1,
            new(TargetCollection, TargetId, VectorField, values ?? [1, 0], Space, 1));

    internal MutationReceipt Apply(ApplyVectorProjection request, Guid? commandId = null)
    {
        var id = commandId ?? Guid.NewGuid();
        var command = new CommandRequest(id, Partition, [request]);
        return Harness.Submit(OperationKind.Batch, command, WorkerId, id).Get<CommitReceipt>().Mutations.Single();
    }

    internal void Reauthorize(ApplyVectorProjection request)
        => Database.Store.Read(view =>
        {
            var principal = Database.Principal(view, WorkerId, Database.EvaluationClock.GetUtcNow());
            Database.ReauthorizeVectorProjection(view, principal, Partition, request);
            return true;
        });

    internal KeyLoadException ApplyFailure(ApplyVectorProjection request)
    {
        var id = Guid.NewGuid();
        var error = Assert.ThrowsExactly<KeyLoadException>(() => Harness.Submit(OperationKind.Batch,
            new CommandRequest(id, Partition, [request]), WorkerId, id).Get<CommitReceipt>());
        return error!;
    }

    internal VectorProjectionLineage? Lineage()
        => Harness.Store.Read(view => view.GetRecord<VectorProjectionLineage>(LineageKey()));

    internal byte[]? VectorBytes()
        => Harness.Store.Read(view => view.ReadOwnedValue(VectorKey()));

    internal byte[] LineageKey(string id = TargetId, string field = VectorField)
        => KeySpace.Partition(VectorProjectionKeys.LineageSpace, Partition, TargetCollection, field, id);

    internal byte[] VectorKey(string id = TargetId, string field = VectorField)
        => KeySpace.Partition("vector", Partition, TargetCollection, field, id);

    internal Task<RankedDocument[]> SearchAsync()
        => new KeyLoad.Query.SearchEngine(Database).SearchAsync(ReaderId,
            new(Partition, TargetCollection, VectorField: VectorField, Vector: [1, 0], Space: Space),
            TestContext.Current!.Execution.CancellationToken);

    public void Dispose() => Harness.Dispose();

    private void ConfigurePrincipal(string id, Capability capabilities, string[] grants, bool revoked = false,
        bool restrictRows = false, string? owner = null, string[]? additionalStreams = null,
        string[]? projects = null)
    {
        var scopes = new List<ScopeGrant> { new(Partition.DatabaseId, Collection, capabilities) };
        if (!string.Equals(TargetCollection, Collection, StringComparison.Ordinal))
        {
            scopes.Add(new(Partition.DatabaseId, TargetCollection, capabilities));
        }
        scopes.Add(new(Partition.DatabaseId, StreamSet, Capability.EventsRead));
        if (additionalStreams is not null)
        {
            scopes.AddRange(additionalStreams.Select(stream => new ScopeGrant(Partition.DatabaseId, stream,
                Capability.EventsRead)));
        }
        var principal = new PrincipalRecord(id, Partition.TenantId,
            [.. scopes],
            [.. grants])
        {
            PolicyEpoch = Database.Store.Read(view =>
                (view.GetRecord<PrincipalRecord>(KeySpace.Principal(id))?.PolicyEpoch ?? 0) + 1),
            OwnerId = owner,
            Projects = [.. projects ?? []],
            RestrictRows = restrictRows,
            Revoked = revoked
        };
        Harness.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal))
            .Get<PrincipalRecord>();
    }
}

internal sealed class EventProjectionHarness : IDisposable
{
    private const string DirectoryPrefix = "keyload-projection-";
    private const string RootId = "root";
    private const string SystemTenant = "system";
    private const string Wildcard = "*";
    private const string RootCredential = "root.projection-test-credential-32-characters";
    private const string PartitionKey = "customer-1";
    private readonly bool bootstrapped;

    internal string DirectoryPath { get; }
    internal ZoneTreeStore Store { get; private set; } = null!;
    internal DatabaseEngine Database { get; private set; } = null!;
    internal PartitionRef Partition { get; } = new("tenant", "database", "orders", PartitionKey);

    internal EventProjectionHarness()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString("N"));
        Open();
        Database.Bootstrap(new(RootId, SystemTenant, [new(Wildcard, Wildcard, Capability.All)], [Wildcard])
        { ClusterAdministrator = true }, DatabaseEngine.Credential(RootId, RootId, RootCredential));
        PhysicalShardTestBootstrap.Bootstrap(Database, RootId);
        bootstrapped = true;
    }

    internal OperationResult Submit<T>(OperationKind kind, T payload, string principal = RootId, Guid? id = null)
    {
        var requestId = id ?? Guid.NewGuid();
        var operation = new ReplicatedOperation(requestId, kind, principal, TimeProvider.System.GetUtcNow(),
            JsonSerializer.Serialize(payload, JsonDefaults.Options));
        return Database.Apply(operation);
    }

    internal ResourceDefinition Configure(string name, ResourceKind kind, SensitiveFieldPolicy[]? fields = null)
    {
        var resource = new ResourceDefinition(name, kind, Partition.TransactionDomainId) { FieldPolicies = [.. fields ?? []] };
        return Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, resource)).Get<ResourceDefinition>();
    }

    internal CommitReceipt Commit(params Mutation[] mutations)
    {
        var id = Guid.NewGuid();
        return Submit(OperationKind.Batch, new CommandRequest(id, Partition, [.. mutations]), id: id)
            .Get<CommitReceipt>();
    }

    internal void Reopen()
    {
        Store.Dispose();
        Open();
        PhysicalShardTestBootstrap.RequireExisting(Database);
    }

    public void Dispose()
    {
        try
        {
            Store.Dispose();
        }
        finally
        {
            if (System.IO.Directory.Exists(DirectoryPath))
            {
                System.IO.Directory.Delete(DirectoryPath, true);
            }
        }
    }

    private void Open()
    {
        if (System.IO.Directory.Exists(DirectoryPath) && !bootstrapped)
        {
            throw new InvalidOperationException("The projection fixture must own a fresh directory.");
        }
        Store = new(new(DirectoryPath), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        Database = new(Store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource());
    }
}
