using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class NativeJournalPrincipalPolicyTests
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
        NativeJournalAuthorityTestBootstrap.Initialize(database.Database);
        var repeated = NativeJournalAuthorityTestBootstrap.Verify(database.Database);
        await Assert.That(repeated.Applied).IsFalse();

        var principal = ReadPrincipal(database);
        await Assert.That(principal).IsNotNull();
        await Assert.That(principal!.Id).IsEqualTo(RuntimeJournalIdentity.ProtectedPrincipalId);
        await Assert.That(principal.TenantId).IsEqualTo(SystemTenantId);
        await Assert.That(principal.ClusterAdministrator).IsFalse();
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
            .Where(credential => credential.PrincipalId == RuntimeJournalIdentity.ProtectedPrincipalId)
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
            KeySpace.Principal(RuntimeJournalIdentity.ProtectedPrincipalId)));
        await Assert.That(replicatedPrincipal).IsNotNull();
        await Assert.That(replicatedPrincipal!.ClusterAdministrator).IsFalse();
        await Assert.That(replicatedPrincipal.Grants).IsEmpty();
        await Assert.That(replicatedPrincipal.PolicyEpoch).IsEqualTo(1L);
    }

    [Test]
    public async Task AcAuth002PublicConfigurationCannotEditProtectedPrincipalOrIssueItsCredential()
    {
        using var database = new TestDatabase();
        NativeJournalAuthorityTestBootstrap.Initialize(database.Database);
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
        await Assert.That(Errors.Code(principalResult.Problem)).IsEqualTo(ErrorCode.PermissionDenied);

        var after = ReadPrincipal(database)!;
        await Assert.That(after.TenantId).IsEqualTo(before.TenantId);
        await Assert.That(after.Grants).IsEmpty();
        await Assert.That(after.FieldGrants).IsEmpty();
        await Assert.That(after.ClusterAdministrator).IsFalse();
        await Assert.That(after.OwnerId).IsNull();
        await Assert.That(after.Projects).IsEmpty();
        await Assert.That(after.RestrictRows).IsFalse();
        await Assert.That(after.Revoked).IsFalse();
        await Assert.That(after.ExpiresAt).IsNull();
        await Assert.That(after.PolicyEpoch).IsEqualTo(1L);

        var keyResult = Apply(database.Database, OperationKind.ConfigureApiKey,
            new ConfigureApiKeyRequest(new ApiKeyRecord(ForbiddenCredentialId,
                RuntimeJournalIdentity.ProtectedPrincipalId, ValidVerifier)), RootPrincipalId);
        await Assert.That(Errors.Code(keyResult.Problem)).IsEqualTo(ErrorCode.PermissionDenied);
        var credential = database.Database.Store.Read(view => view.GetRecord<ApiKeyRecord>(KeySpace.ApiKey(ForbiddenCredentialId)));
        await Assert.That(credential).IsNull();
    }

    private static PrincipalRecord? ReadPrincipal(TestDatabase database)
        => database.Database.Store.Read(view => view.GetRecord<PrincipalRecord>(
            KeySpace.Principal(RuntimeJournalIdentity.ProtectedPrincipalId)));

    private static Result<object> Apply(DatabaseEngine database, OperationKind kind, object payload, string principalId,
        long replicationIndex = 0)
        => database.Apply(database.CreateCommand(kind, Guid.NewGuid(), principalId, OperationTime, JsonSerializer.Serialize(payload, payload.GetType(), JsonDefaults.Options)), replicationIndex);
}
