using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class FollowerDocumentFixture : IDisposable
{
    internal const string Root = "root";
    internal const string Reader = "follower-reader";
    internal const string KeyId = "follower-read-key";
    internal const string Secret = "follower-read-key.unit-secret-private-32-characters";
    internal const string Collection = "follower-documents";
    internal const string DocumentId = "point";
    internal const string OriginalJson = "{\"title\":\"old\",\"secret\":\"private-stale-sentinel\"}";
    internal const string FreshJson = "{\"title\":\"fresh\",\"secret\":\"private-fresh-sentinel\"}";
    internal const string RedactedOriginalJson = "{\"title\":\"old\"}";
    internal const long FirstRevision = 1;
    internal const long SecondRevision = 2;
    internal const long Term = 1;
    internal const int Version = 1;
    private const int SnapshotLimit = 128;

    internal TestDatabase Db { get; } = new();
    internal EntityRef Reference => new(Db.Partition, Collection, DocumentId);
    internal PrincipalRecord Principal { get; }
    internal ApiKeyRecord Credential { get; }
    internal ResourceDefinition Resource { get; private set; }
    internal PhysicalShardRecord Owner { get; }
    internal string ReplicaId => Owner.VoterIds[0];
    internal long Position { get; private set; }

    internal FollowerDocumentFixture()
    {
        try
        {
            Resource = new(Collection, ResourceKind.Collection, Db.Partition.TransactionDomainId);
            Apply(OperationKind.ConfigureResource, new ConfigureResourceRequest(Db.Partition.TenantId,
                Db.Partition.DatabaseId, Resource)).Get<ResourceDefinition>();
            Principal = new(Reader, Db.Partition.TenantId,
                [new(Db.Partition.DatabaseId, Collection, Capability.DocumentsRead | Capability.DocumentsWrite)], [])
            { ClusterAdministrator = false, PolicyEpoch = FirstRevision };
            Apply(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(Principal)).Get<PrincipalRecord>();
            Credential = DatabaseEngine.Credential(KeyId, Reader, Secret);
            Apply(OperationKind.ConfigureApiKey, new ConfigureApiKeyRequest(Credential)).Get<bool>();
            Put(OriginalJson, 0);
            Owner = Db.Database.ReadPhysicalShardCatalog(Root).DefaultShard;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            KeyLoad.Server.ServerFailureObserver.Observe(Db.Dispose, failures);
            KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal OperationResult Apply<T>(OperationKind kind, T payload)
    {
        var operation = Db.Database.CreateNativeOperation(kind, Guid.NewGuid(), Root,
            Db.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(payload));
        return Db.Database.Apply(operation, checked(++Position));
    }

    internal CommitReceipt Put(string json, long revision, RowAccess? access = null)
    {
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, Db.Partition,
            [new PutDocument(Collection, DocumentId, json, revision, access, ExplicitReplacement: revision > 0)]);
        var operation = Db.Database.CreateNativeOperation(OperationKind.Batch, id, Root,
            Db.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(command));
        return Db.Database.Apply(operation, checked(++Position)).Get<CommitReceipt>();
    }

    internal CommitReceipt Delete(long revision)
    {
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, Db.Partition, [new DeleteDocument(Collection, DocumentId, revision)]);
        var operation = Db.Database.CreateNativeOperation(OperationKind.Batch, id, Root,
            Db.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(command));
        return Db.Database.Apply(operation, checked(++Position)).Get<CommitReceipt>();
    }

    internal ReadFollowerDocumentRequestV1 Request(long lag = long.MaxValue, CommitToken? minimum = null)
        => new(Version, Reference, ReplicaId, lag, minimum);

    internal FollowerDocumentSnapshot Capture(ReadFollowerDocumentRequestV1 request, CancellationToken token = default)
        => Db.Database.CaptureFollowerDocument(Reader, request, Owner, ReplicaId, Term, token);

    internal FollowerDocumentReadResultV1 Complete(ReadFollowerDocumentRequestV1 request,
        FollowerDocumentSnapshot snapshot, CancellationToken token = default)
        => Db.Database.CompleteFollowerDocument(Reader,
            new(request, DatabaseEngine.IssueCredentialWitness(Secret)), snapshot, ReplicaId, Term, token);

    internal void ChangePolicy()
    {
        var replacement = Resource with
        {
            SchemaVersion = Resource.SchemaVersion + 1,
            FieldPolicies = [new("/secret", "private", "secret.read", "secret.use", "secret.write")]
        };
        Resource = Apply(OperationKind.ConfigureResource, new ConfigureResourceRequest(Db.Partition.TenantId,
            Db.Partition.DatabaseId, replacement)
        { ExpectedSchemaVersion = Resource.SchemaVersion }).Get<ResourceDefinition>();
    }

    internal byte[] CredentialBytes() => Db.Store.Read(view => view.ReadOwnedValue(KeySpace.ApiKey(KeyId)))
        ?? throw new InvalidOperationException("The native fixture credential is missing.");

    internal byte[] AppliedBytes() => Db.Store.Read(view => view.ReadOwnedValue(KeySpace.AppliedBytes))
        ?? throw new InvalidOperationException("The native fixture applied cut is missing.");

    internal (string Key, string Value)[] State() => Db.Store.Read(view =>
    {
        var page = view.Scan([], SnapshotLimit);
        if (page.HasMore)
        { throw new InvalidOperationException("The complete follower fixture exceeds its native bound."); }
        return page.Records.Select(record => (Convert.ToHexString(record.Key.Span),
            Convert.ToHexString(record.Value.Span))).ToArray();
    });

    public void Dispose() => Db.Dispose();
}
