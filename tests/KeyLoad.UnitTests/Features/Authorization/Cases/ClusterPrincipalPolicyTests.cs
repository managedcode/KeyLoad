using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class ClusterPrincipalPolicyTests
{
    private const string ApiKeyStorageSpace = "api-key";
    private const string RootPrincipalId = "root";
    private const string SystemTenantId = "system";
    private const string AttackerTenantId = "attacker-tenant";
    private const string WildcardScope = "*";
    private const string ForbiddenCredentialId = "forbidden-cluster-credential";
    private const string ValidVerifier = "0000000000000000000000000000000000000000000000000000000000000000";
    private static readonly DateTimeOffset OperationTime = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AcAuth002InitializationSeedsAnIdempotentProtectedPrincipalAndSnapshotCopiesIt()
    {
        using var database = new TestDatabase();
        ClusterPrincipalPolicy.Initialize(database.Database);
        ClusterPrincipalPolicy.Initialize(database.Database);

        var principal = ReadPrincipal(database);
        await Assert.That(principal).IsNotNull();
        await Assert.That(principal!.Id).IsEqualTo(ClusterPrincipalPolicy.InternalPrincipalId);
        await Assert.That(principal.TenantId).IsEqualTo(SystemTenantId);
        await Assert.That(principal.ClusterAdministrator).IsTrue();
        await Assert.That(principal.Grants).IsEmpty();
        await Assert.That(principal.FieldGrants).IsEmpty();
        await Assert.That(principal.PolicyEpoch).IsEqualTo(1L);
        await Assert.That(principal.OwnerId).IsNull();
        await Assert.That(principal.Projects).IsEmpty();
        await Assert.That(principal.RestrictRows).IsFalse();
        await Assert.That(principal.ExpiresAt).IsNull();
        await Assert.That(principal.Revoked).IsFalse();

        var storedCredentials = database.Database.Store.Read(view => view
            .Scan(KeyCodec.Encode(ApiKeyStorageSpace), 100)
            .Records
            .Select(record => NativeSerialization.Deserialize<ApiKeyRecord>(record.Value.Span))
            .Where(credential => credential.PrincipalId == ClusterPrincipalPolicy.InternalPrincipalId)
            .ToArray());
        await Assert.That(storedCredentials).IsEmpty();

        var snapshotPath = Path.Combine(database.Directory, "authorization.snapshot");
        database.Database.Store.CreateSnapshot(snapshotPath, expectedAppliedPosition: 0);
        var replicaDirectory = Path.Combine(database.Directory, "replica");
        using var replica = new ZoneTreeStore(new ZoneTreeStoreOptions(replicaDirectory)
        {
            Incarnation = database.Store.Identity.Incarnation,
            SigningKey = database.Store.Identity.SigningKey
        }, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        replica.InstallSnapshot(snapshotPath, expectedAppliedPosition: 0);
        var replicatedPrincipal = replica.Read(view => view.GetRecord<PrincipalRecord>(
            KeySpace.Principal(ClusterPrincipalPolicy.InternalPrincipalId)));
        await Assert.That(replicatedPrincipal).IsNotNull();
        await Assert.That(replicatedPrincipal!.ClusterAdministrator).IsTrue();
        await Assert.That(replicatedPrincipal.Grants).IsEmpty();
        await Assert.That(replicatedPrincipal.PolicyEpoch).IsEqualTo(1L);
    }

    [Test]
    public async Task AcAuth002PublicConfigurationCannotEditProtectedPrincipalOrIssueItsCredential()
    {
        using var database = new TestDatabase();
        ClusterPrincipalPolicy.Initialize(database.Database);
        var before = ReadPrincipal(database)!;
        var attemptedPrincipal = before with
        {
            TenantId = AttackerTenantId,
            Grants = [new ScopeGrant(WildcardScope, WildcardScope, Capability.All)],
            FieldGrants = [WildcardScope],
            ClusterAdministrator = false,
            OwnerId = AttackerTenantId,
            Projects = [AttackerTenantId],
            RestrictRows = true,
            Revoked = true,
            ExpiresAt = OperationTime,
            PolicyEpoch = 2
        };

        var principalResult = Apply(database.Database, OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(attemptedPrincipal), RootPrincipalId);
        await Assert.That(principalResult.Error).IsEqualTo(ErrorCode.PermissionDenied);

        var after = ReadPrincipal(database)!;
        await Assert.That(after.TenantId).IsEqualTo(before.TenantId);
        await Assert.That(after.Grants).IsEmpty();
        await Assert.That(after.FieldGrants).IsEmpty();
        await Assert.That(after.ClusterAdministrator).IsTrue();
        await Assert.That(after.OwnerId).IsNull();
        await Assert.That(after.Projects).IsEmpty();
        await Assert.That(after.RestrictRows).IsFalse();
        await Assert.That(after.Revoked).IsFalse();
        await Assert.That(after.ExpiresAt).IsNull();
        await Assert.That(after.PolicyEpoch).IsEqualTo(1L);

        var keyResult = Apply(database.Database, OperationKind.ConfigureApiKey,
            new ConfigureApiKeyRequest(new ApiKeyRecord(ForbiddenCredentialId,
                ClusterPrincipalPolicy.InternalPrincipalId, ValidVerifier)), RootPrincipalId);
        await Assert.That(keyResult.Error).IsEqualTo(ErrorCode.PermissionDenied);
        var credential = database.Database.Store.Read(view => view.GetRecord<ApiKeyRecord>(KeySpace.ApiKey(ForbiddenCredentialId)));
        await Assert.That(credential).IsNull();
    }

    private static PrincipalRecord? ReadPrincipal(TestDatabase database)
        => database.Database.Store.Read(view => view.GetRecord<PrincipalRecord>(
            KeySpace.Principal(ClusterPrincipalPolicy.InternalPrincipalId)));

    private static OperationResult Apply(DatabaseEngine database, OperationKind kind, object payload, string principalId,
        long replicationIndex = 0)
        => database.Apply(new ReplicatedOperation(Guid.NewGuid(), kind, principalId, OperationTime,
            JsonSerializer.Serialize(payload, payload.GetType(), JsonDefaults.Options)), replicationIndex);
}

internal sealed class ClusterPrincipalAuthorizationTests
{
    private const string MembershipKey = "authorization-membership";
    private const string MembershipStorageSpace = "membership";
    private const string RootPrincipalId = "root";
    private const string OrdinaryPrincipalId = "ordinary-principal";
    private const string EmptyJson = "{}";
    private const string CollectionName = "orders";
    private const string DocumentStorageSpace = "document";
    private const string ProtectedDocumentId = "internal-write";
    private const string ForbiddenCredentialId = "forbidden-cluster-credential";
    private const string ValidVerifier = "0000000000000000000000000000000000000000000000000000000000000000";
    private static readonly DateTimeOffset OperationTime = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AcAuth003OrdinaryClusterAdministratorCannotApplyMembership()
    {
        using var database = new TestDatabase();
        ClusterPrincipalPolicy.Initialize(database.Database);
        var ordinary = new PrincipalRecord(OrdinaryPrincipalId, database.Partition.TenantId, [], []);
        Apply(database.Database, OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(ordinary), RootPrincipalId)
            .Get<PrincipalRecord>();

        var ordinaryResult = Apply(database.Database, OperationKind.Membership,
            new MembershipMutation(MembershipKey, 0, NativeSerialization.Serialize(EmptyJson)), OrdinaryPrincipalId);
        var administratorResult = Apply(database.Database, OperationKind.Membership,
            new MembershipMutation(MembershipKey, 0, NativeSerialization.Serialize(EmptyJson)), RootPrincipalId);

        await Assert.That(ordinaryResult.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(administratorResult.Error).IsEqualTo(ErrorCode.PermissionDenied);
        var membership = database.Database.Store.Read(view => view.GetRecord<MembershipRecord>(
            KeyCodec.Encode(MembershipStorageSpace, MembershipKey)));
        await Assert.That(membership).IsNull();
    }

    [Test]
    public async Task AcAuth003InternalPrincipalCannotApplyDataOrPublicSecurityOperations()
    {
        using var database = new TestDatabase();
        ClusterPrincipalPolicy.Initialize(database.Database);
        var resource = new ResourceDefinition(CollectionName, ResourceKind.Collection, database.Partition.TransactionDomainId);
        Apply(database.Database, OperationKind.ConfigureResource,
            new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, resource), RootPrincipalId)
            .Get<ResourceDefinition>();
        var ordinary = new PrincipalRecord(OrdinaryPrincipalId, database.Partition.TenantId, [], []);
        Apply(database.Database, OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(ordinary), RootPrincipalId)
            .Get<PrincipalRecord>();

        var commandId = Guid.NewGuid();
        var batchResult = Apply(database.Database, OperationKind.Batch,
            new CommandRequest(commandId, database.Partition, [new PutDocument(CollectionName, ProtectedDocumentId, EmptyJson, 0)]),
            ClusterPrincipalPolicy.InternalPrincipalId);
        var principalResult = Apply(database.Database, OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(ordinary with { Revoked = true, PolicyEpoch = ordinary.PolicyEpoch + 1 }),
            ClusterPrincipalPolicy.InternalPrincipalId);
        var credentialResult = Apply(database.Database, OperationKind.ConfigureApiKey,
            new ConfigureApiKeyRequest(new ApiKeyRecord(ForbiddenCredentialId, RootPrincipalId, ValidVerifier)),
            ClusterPrincipalPolicy.InternalPrincipalId);

        await Assert.That(batchResult.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(principalResult.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(credentialResult.Error).IsEqualTo(ErrorCode.PermissionDenied);
        var document = database.Database.Store.Read(view => view.GetRecord<DocumentRecord>(KeySpace.Partition(
            DocumentStorageSpace, database.Partition, CollectionName, ProtectedDocumentId)));
        var unchangedPrincipal = database.Database.Store.Read(view => view.GetRecord<PrincipalRecord>(
            KeySpace.Principal(OrdinaryPrincipalId)));
        var credential = database.Database.Store.Read(view => view.GetRecord<ApiKeyRecord>(KeySpace.ApiKey(ForbiddenCredentialId)));
        await Assert.That(document).IsNull();
        await Assert.That(unchangedPrincipal).IsNotNull();
        await Assert.That(unchangedPrincipal!.Revoked).IsFalse();
        await Assert.That(credential).IsNull();
    }

    [Test]
    public async Task AcAuth003InternalMembershipWorksAfterPublicRootRevocation()
    {
        using var database = new TestDatabase();
        ClusterPrincipalPolicy.Initialize(database.Database);
        var root = database.Database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(RootPrincipalId)))!;
        var revokeResult = Apply(database.Database, OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(root with { Revoked = true, PolicyEpoch = root.PolicyEpoch + 1 }), RootPrincipalId, 1);
        await Assert.That(revokeResult.Error).IsNull();

        var membershipResult = Apply(database.Database, OperationKind.Membership,
            new MembershipMutation(MembershipKey, 0, NativeSerialization.Serialize(EmptyJson)), ClusterPrincipalPolicy.InternalPrincipalId, 2);
        await Assert.That(membershipResult.Error).IsNull();
        await Assert.That(membershipResult.Get<bool>()).IsTrue();

        var membership = database.Database.Store.Read(view => view.GetRecord<MembershipRecord>(
            KeyCodec.Encode(MembershipStorageSpace, MembershipKey)));
        await Assert.That(membership).IsNotNull();
        await Assert.That(membership!.Version).IsEqualTo(1L);
        await Assert.That(NativeSerialization.Deserialize<string>(membership.Payload.Span)).IsEqualTo(EmptyJson);
        var revokedRoot = database.Database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(RootPrincipalId)));
        await Assert.That(revokedRoot!.Revoked).IsTrue();
    }

    private static OperationResult Apply(DatabaseEngine database, OperationKind kind, object payload, string principalId,
        long replicationIndex = 0)
        => database.Apply(new ReplicatedOperation(Guid.NewGuid(), kind, principalId, OperationTime,
            JsonSerializer.Serialize(payload, payload.GetType(), JsonDefaults.Options)), replicationIndex);
}

internal sealed class ClusterPrincipalInitializationIntegrityTests
{
    private const string RootPrincipalId = "root";
    private const string AppliedCutResourceName = "authorization-cut";
    private static readonly DateTimeOffset OperationTime = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly byte[] InvalidPrincipalBytes = [0xFF, 0x00, 0x01];

    [Test]
    public async Task AcAuth002MissingProtectedPrincipalAtExistingCutRequiresRecovery()
    {
        using var database = new TestDatabase();
        ClusterPrincipalPolicy.Initialize(database.Database);
        AdvanceAppliedCut(database);
        database.Store.Commit((transaction, _) =>
        {
            transaction.Delete(KeySpace.Principal(ClusterPrincipalPolicy.InternalPrincipalId));
            return true;
        });

        var exception = Assert.ThrowsExactly<KeyLoadException>(() => ClusterPrincipalPolicy.Initialize(database.Database));
        await Assert.That(exception.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(ReadPrincipal(database)).IsNull();
    }

    [Test]
    public async Task AcAuth002ModifiedProtectedPrincipalAtExistingCutRequiresRecovery()
    {
        using var database = new TestDatabase();
        ClusterPrincipalPolicy.Initialize(database.Database);
        AdvanceAppliedCut(database);
        database.Store.Commit((transaction, _) =>
        {
            var key = KeySpace.Principal(ClusterPrincipalPolicy.InternalPrincipalId);
            var principal = transaction.GetRecord<PrincipalRecord>(key)!;
            transaction.PutRecord(key, principal with { PolicyEpoch = principal.PolicyEpoch + 1 });
            return true;
        });

        var exception = Assert.ThrowsExactly<KeyLoadException>(() => ClusterPrincipalPolicy.Initialize(database.Database));
        await Assert.That(exception.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.That(ReadPrincipal(database)!.PolicyEpoch).IsEqualTo(2L);
    }

    [Test]
    public async Task AcAuth002CorruptProtectedPrincipalAtExistingCutRequiresRecovery()
    {
        using var database = new TestDatabase();
        ClusterPrincipalPolicy.Initialize(database.Database);
        AdvanceAppliedCut(database);
        database.Store.Commit((transaction, _) =>
        {
            transaction.Put(KeySpace.Principal(ClusterPrincipalPolicy.InternalPrincipalId), InvalidPrincipalBytes);
            return true;
        });

        var exception = Assert.ThrowsExactly<KeyLoadException>(() => ClusterPrincipalPolicy.Initialize(database.Database));
        await Assert.That(exception.Code).IsEqualTo(ErrorCode.RecoveryRequired);
    }

    private static PrincipalRecord? ReadPrincipal(TestDatabase database)
        => database.Database.Store.Read(view => view.GetRecord<PrincipalRecord>(
            KeySpace.Principal(ClusterPrincipalPolicy.InternalPrincipalId)));

    private static void AdvanceAppliedCut(TestDatabase database)
    {
        var request = new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId,
            new ResourceDefinition(AppliedCutResourceName, ResourceKind.Collection, database.Partition.TransactionDomainId));
        Apply(database.Database, OperationKind.ConfigureResource, request, RootPrincipalId, 1).Get<ResourceDefinition>();
    }

    private static OperationResult Apply(DatabaseEngine database, OperationKind kind, object payload, string principalId,
        long replicationIndex = 0)
        => database.Apply(new ReplicatedOperation(Guid.NewGuid(), kind, principalId, OperationTime,
            JsonSerializer.Serialize(payload, payload.GetType(), JsonDefaults.Options)), replicationIndex);
}
