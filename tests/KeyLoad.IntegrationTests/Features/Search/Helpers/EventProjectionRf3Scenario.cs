using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record EventProjectionRf3Scenario(PartitionRef Partition, StreamRef Stream,
    string WorkerSecret, string ReaderSecret, Guid CommandId, ApplyVectorProjection Projection)
{
    internal const string Collection = "event-projection-rf3";
    internal const string StreamSet = "event-projection-rf3-events";
    internal const string StreamId = "source-stream";
    internal const string SourceId = "source";
    internal const string TargetId = "derived";
    internal const string BaselineId = "baseline";
    internal const string InputField = "/input";
    internal const string VectorField = "/embedding";
    internal const string SourceClass = "event-projection-private";
    internal const string ChangedSourceClass = "event-projection-reclassified";
    internal const string InputReadGrant = "event-projection.input.read";
    internal const string InputUseGrant = "event-projection.input.use";
    internal const string InputWriteGrant = "event-projection.input.write";
    internal const string VectorUseGrant = "event-projection.vector.use";
    internal const string VectorWriteGrant = "event-projection.vector.write";
    internal const string SourceOwner = "event-source-owner";
    internal const string TargetOwner = "event-target-owner";
    internal const string ReducerId = "event-projection-reducer";
    internal const string ReducerVersion = "v1";
    internal const string MutationKind = "applyVectorProjection";
    internal const string Secret = "projection-source-private-canary";
    internal const string EventId = "source-event-1";
    internal const string EventType = "SourceDocumentUpdated";
    internal const string FailureScenario = "event-projection-lineage-leader-loss";
    internal const string RestartFailureKey = "eventProjectionRestartFailure";
    internal const string DiagnosticsFailureKey = "eventProjectionDiagnosticsFailure";
    internal const string MissingLeader = "The RF3 cluster did not advertise a leader.";
    internal const int Revision = 1;
    internal const long Generation = 1;
    internal const int NodeCount = 3;
    internal static VectorSpace Space { get; } = new("event-projection-rf3-space", 2,
        DistanceMetric.Cosine, "event-projection-rf3-model", ReducerVersion);

    internal static async Task<EventProjectionRf3Scenario> CreateAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var partition = new PartitionRef("event-projection-tenant-" + Guid.NewGuid().ToString("N"),
            "event-projection-database", "event-projection-domain", Guid.NewGuid().ToString("N"));
        var stream = new StreamRef(partition, StreamSet, StreamId, Generation);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey);
        await ConfigureResourcesAsync(administrator, partition, cancellationToken);
        var worker = await ConfigureWorkerAsync(administrator, partition, cancellationToken);
        var reader = await ConfigureReaderAsync(administrator, partition, false, null, cancellationToken);
        await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(Seed(partition), cancellationToken));
        var target = new PutVector(Collection, TargetId, VectorField, [1, 0], Space, Revision);
        var projection = new ApplyVectorProjection(stream, Revision, EventId,
            new(partition, Collection, SourceId), Revision, InputField, ReducerId, ReducerVersion,
            Generation, target);
        return new(partition, stream, worker.Secret, reader.Secret, Guid.NewGuid(), projection);
    }

    internal CommandRequest ApplyCommand(Guid? commandId = null, ApplyVectorProjection? projection = null)
        => new(commandId ?? CommandId, Partition, [projection ?? Projection]);

    internal SearchRequest VectorSearch()
        => new(Partition, Collection, VectorField: VectorField, Vector: [1, 0], Space: Space,
            Limit: 10, FusionConstant: 60);

    internal static async Task<McpPersistedIdentity> CreateReaderAsync(ClusterFixture fixture,
        PartitionRef partition, bool restrictRows, string? owner, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        return await ConfigureReaderAsync(new KeyLoadClient(http, fixture.AdminKey), partition,
            restrictRows, owner, cancellationToken);
    }

    internal static async Task ReclassifySourceAsync(ClusterFixture fixture, PartitionRef partition,
        CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey);
        var resource = Resource(partition, ChangedSourceClass) with { SchemaVersion = 2 };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, resource) { ExpectedSchemaVersion = 1 }, cancellationToken));
    }

    internal CommandRequest ChangeSourceCommand()
        => new(Guid.NewGuid(), Partition,
        [new PatchDocument(Collection, SourceId,
            [new(InputField, PatchKind.Set, "\"updated source value\"")], Revision)]);

    private static async Task ConfigureResourcesAsync(KeyLoadClient administrator, PartitionRef partition,
        CancellationToken cancellationToken)
    {
        var streamSet = new ResourceDefinition(StreamSet, ResourceKind.StreamSet, partition.TransactionDomainId);
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, streamSet), cancellationToken));
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, Resource(partition, SourceClass)), cancellationToken));
    }

    private static ResourceDefinition Resource(PartitionRef partition, string sourceClass)
        => new(Collection, ResourceKind.Collection, partition.TransactionDomainId)
        {
            FieldPolicies =
            [
                new(InputField, sourceClass, InputReadGrant, InputUseGrant, InputWriteGrant),
                new(VectorField, SourceClass, RawUseGrant: VectorUseGrant, WriteGrant: VectorWriteGrant)
            ]
        };

    private static CommandRequest Seed(PartitionRef partition)
        => new(Guid.NewGuid(), partition,
        [
            new PutDocument(Collection, SourceId, "{\"input\":\"" + Secret + "\"}", Access: new(SourceOwner)),
            new PutDocument(Collection, TargetId, "{\"kind\":\"derived\"}", Access: new(TargetOwner)),
            new PutDocument(Collection, BaselineId, "{\"kind\":\"baseline\"}", Access: new(TargetOwner)),
            new PutVector(Collection, BaselineId, VectorField, [0, 1], Space, Revision),
            new AppendEvents(StreamSet, StreamId,
                [new(EventId, EventType, "{\"sourceRevision\":1}")], ExpectedStreamRevision.NoStream,
                Generation)
        ]);

    private static async Task<McpPersistedIdentity> ConfigureWorkerAsync(
        KeyLoadClient administrator, PartitionRef partition, CancellationToken cancellationToken)
    {
        var principal = new PrincipalRecord("event-projection-worker-" + Guid.NewGuid().ToString("N"),
            partition.TenantId,
            [new(partition.DatabaseId, Collection, Capability.DocumentsRead | Capability.DocumentsWrite),
                new(partition.DatabaseId, StreamSet, Capability.EventsRead)],
            [InputReadGrant, InputUseGrant, InputWriteGrant, VectorUseGrant, VectorWriteGrant]);
        return await ConfigureIdentityAsync(administrator, principal, cancellationToken);
    }

    private static async Task<McpPersistedIdentity> ConfigureReaderAsync(KeyLoadClient administrator,
        PartitionRef partition, bool restrictRows, string? owner, CancellationToken cancellationToken)
    {
        var principal = new PrincipalRecord("event-projection-reader-" + Guid.NewGuid().ToString("N"),
            partition.TenantId,
            [new(partition.DatabaseId, Collection,
                Capability.Query | Capability.DocumentsRead | Capability.VectorSearch)],
            [InputUseGrant, VectorUseGrant])
        {
            RestrictRows = restrictRows,
            OwnerId = owner
        };
        var identity = await ConfigureIdentityAsync(administrator, principal, cancellationToken);
        return new(principal, identity.Credential, identity.Secret);
    }

    private static async Task<McpPersistedIdentity> ConfigureIdentityAsync(KeyLoadClient administrator,
        PrincipalRecord principal, CancellationToken cancellationToken)
    {
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(),
            principal, cancellationToken));
        var keyId = "event-projection-key-" + Guid.NewGuid().ToString("N");
        var secret = keyId + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var verifier = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureApiKeyAsync(Guid.NewGuid(),
            new(keyId, principal.Id, verifier), cancellationToken));
        return new(principal, new(keyId, principal.Id, verifier), secret);
    }
}
