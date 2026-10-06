using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnSeedAuthorityTests
{
    private const string ReadOnlyPrincipal = "seed-no-use";
    private const string MissingCapabilityPrincipal = "seed-no-vector";
    private const string RevokedPrincipal = "seed-revoked";
    private const string ExpiredPrincipal = "seed-expired";
    private const string InvalidIdentifier = "\u0001bad";
    private const string ForeignTenant = "foreign-tenant";
    private const string ForeignPartitionKey = "foreign-partition";
    private const string OtherDomain = "other-domain";

    [Test]
    public async Task CaptureRequiresOnlyPersistedVectorSearchAndFieldUse()
    {
        using var database = AnnSeedTestSupport.Create();
        var seed = AnnSeedTestSupport.Capture(database);

        await Assert.That(seed.Scope.PrincipalId).IsEqualTo(AnnSeedTestSupport.Principal);
        await Assert.That(seed.Scope.PolicyEpoch).IsEqualTo(1L);
        await Assert.That(seed.Records.Length).IsEqualTo(3);
        await Assert.That(seed.Scope.Space).IsEqualTo(AnnSeedTestSupport.Space());

        AnnSeedTestSupport.Persist(database, ReadOnlyPrincipal, Capability.VectorSearch, []);
        await Assert.That(AnnSeedTestSupport.CaptureFailure(database, ReadOnlyPrincipal).Code)
            .IsEqualTo(ErrorCode.PermissionDenied);

        AnnSeedTestSupport.Persist(database, MissingCapabilityPrincipal, Capability.None, [AnnSeedTestSupport.FieldUse]);
        await Assert.That(AnnSeedTestSupport.CaptureFailure(database, MissingCapabilityPrincipal).Code)
            .IsEqualTo(ErrorCode.PermissionDenied);
    }

    [Test]
    public async Task CaptureRejectsRevokedExpiredAndInvalidScopeInputs()
    {
        using var database = AnnSeedTestSupport.Create();
        AnnSeedTestSupport.Persist(database, RevokedPrincipal, Capability.VectorSearch,
            [AnnSeedTestSupport.FieldUse], revoked: true);
        AnnSeedTestSupport.Persist(database, ExpiredPrincipal, Capability.VectorSearch,
            [AnnSeedTestSupport.FieldUse], expiresAt: TimeProvider.System.GetUtcNow().AddMinutes(-5));

        await Assert.That(AnnSeedTestSupport.CaptureFailure(database, RevokedPrincipal).Code)
            .IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(AnnSeedTestSupport.CaptureFailure(database, ExpiredPrincipal).Code)
            .IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(AnnSeedTestSupport.CaptureFailure(database, "missing-seed-principal").Code)
            .IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(AnnSeedTestSupport.CaptureFailure(database, AnnSeedTestSupport.Principal,
            AnnSeedTestSupport.Space(id: InvalidIdentifier)).Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(AnnSeedTestSupport.CaptureFailure(database, AnnSeedTestSupport.Principal,
            AnnSeedTestSupport.Space(dimension: 0)).Code).IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    public async Task PersistedTenantBoundaryDeniesChildAndAllowsClusterAdministrator()
    {
        using var database = AnnSeedTestSupport.Create(0);
        var foreignPartition = new PartitionRef(ForeignTenant, database.Partition.DatabaseId,
            database.Partition.TransactionDomainId, ForeignPartitionKey);
        var foreignResource = new ResourceDefinition(AnnSeedTestSupport.Collection, ResourceKind.Collection,
            foreignPartition.TransactionDomainId)
        {
            FieldPolicies = [new(AnnSeedTestSupport.Field, "embedding", AnnSeedTestSupport.FieldRead,
                AnnSeedTestSupport.FieldUse, AnnSeedTestSupport.FieldWrite)]
        };
        database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(ForeignTenant,
            foreignPartition.DatabaseId, foreignResource)).Get<ResourceDefinition>();
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));

        var denied = Assert.ThrowsExactly<KeyLoadException>(() => AnnSeedCollector.Capture(database.Database,
            AnnSeedTestSupport.Principal, foreignPartition, AnnSeedTestSupport.Collection,
            AnnSeedTestSupport.Field, AnnSeedTestSupport.Space(), UnitAnnSeedOptions.Execution(new()), budget));
        var administratorSeed = AnnSeedCollector.Capture(database.Database, "root", foreignPartition,
            AnnSeedTestSupport.Collection, AnnSeedTestSupport.Field, AnnSeedTestSupport.Space(), UnitAnnSeedOptions.Execution(new()), new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits)));

        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(administratorSeed.Scope.PrincipalId).IsEqualTo("root");
        await Assert.That(administratorSeed.Scope.Partition).IsEqualTo(foreignPartition);
        await Assert.That(administratorSeed.Records).IsEmpty();

        var wrongDomain = database.Partition with { TransactionDomainId = OtherDomain };
        var domainFailure = Assert.ThrowsExactly<KeyLoadException>(() => AnnSeedCollector.Capture(
            database.Database, AnnSeedTestSupport.Principal, wrongDomain, AnnSeedTestSupport.Collection,
            AnnSeedTestSupport.Field, AnnSeedTestSupport.Space(), UnitAnnSeedOptions.Execution(new()), new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits))));
        await Assert.That(domainFailure.Code).IsEqualTo(ErrorCode.Conflict);
        var absentResource = Assert.ThrowsExactly<KeyLoadException>(() => AnnSeedCollector.Capture(
            database.Database, "root", database.Partition, "absent-collection", AnnSeedTestSupport.Field,
            AnnSeedTestSupport.Space(), UnitAnnSeedOptions.Execution(new()), new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits))));
        await Assert.That(absentResource.Code).IsEqualTo(ErrorCode.NotFound);
    }

    [Test]
    public async Task ExistingSeedDoesNotAuthorizeFreshCaptureAfterPersistedPolicyRevocation()
    {
        using var database = AnnSeedTestSupport.Create();
        var historical = AnnSeedTestSupport.Capture(database);
        var prior = database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(AnnSeedTestSupport.Principal)))!;
        AnnSeedTestSupport.Persist(database, prior.Id, Capability.VectorSearch, [], owner: prior.OwnerId,
            restrictRows: prior.RestrictRows, policyEpoch: prior.PolicyEpoch + 1);

        await Assert.That(historical.Records.Length).IsEqualTo(3);
        await Assert.That(AnnSeedTestSupport.CaptureFailure(database, AnnSeedTestSupport.Principal).Code)
            .IsEqualTo(ErrorCode.PermissionDenied);
    }
}
