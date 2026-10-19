using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.RuntimeJournaling.Identity;
using KeyLoad.Storage;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class JournalPrincipalAuthorizationTests
{
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
    public async Task AcAuth003OrdinaryAndClusterAdministratorCannotMutatePrivateJournal()
    {
        using var database = new TestDatabase();
        JournalAuthorityTestBootstrap.Initialize(database.Database);
        var ordinary = new PrincipalRecord(OrdinaryPrincipalId, database.Partition.TenantId, [], []);
        Apply(database.Database, OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(ordinary), RootPrincipalId)
            .Get<PrincipalRecord>();

        var ordinaryResult = Apply(database.Database, OperationKind.RuntimeJournal,
            JournalAuthorityTestBootstrap.CreateMutation(), OrdinaryPrincipalId);
        var administratorResult = Apply(database.Database, OperationKind.RuntimeJournal,
            JournalAuthorityTestBootstrap.CreateMutation(), RootPrincipalId);

        await Assert.That(Errors.Code(ordinaryResult.Problem)).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(Errors.Code(administratorResult.Problem)).IsEqualTo(ErrorCode.PermissionDenied);
        var catalog = database.Database.ReadRuntimeJournalCatalog(RuntimeJournalIdentity.ProtectedPrincipalId);
        await Assert.That(catalog.Journals).IsEmpty();
    }

    [Test]
    public async Task AcAuth003InternalPrincipalCannotApplyDataOrPublicSecurityOperations()
    {
        using var database = new TestDatabase();
        JournalAuthorityTestBootstrap.Initialize(database.Database);
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
            RuntimeJournalIdentity.ProtectedPrincipalId);
        var principalResult = Apply(database.Database, OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(ordinary with
            {
                Revoked = true,
                PolicyEpoch = ordinary.PolicyEpoch + 1
            }),
            RuntimeJournalIdentity.ProtectedPrincipalId);
        var credentialResult = Apply(database.Database, OperationKind.ConfigureApiKey,
            new ConfigureApiKeyRequest(new ApiKeyRecord(ForbiddenCredentialId, RootPrincipalId, ValidVerifier)),
            RuntimeJournalIdentity.ProtectedPrincipalId);

        await Assert.That(Errors.Code(batchResult.Problem)).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(Errors.Code(principalResult.Problem)).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(Errors.Code(credentialResult.Problem)).IsEqualTo(ErrorCode.PermissionDenied);
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
    public async Task AcAuth003JournalWorksAfterPublicRootRevocation()
    {
        using var database = new TestDatabase();
        JournalAuthorityTestBootstrap.Initialize(database.Database);
        var root = database.Database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(RootPrincipalId)))!;
        var revokeResult = Apply(database.Database, OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(root with
            {
                Revoked = true,
                PolicyEpoch = root.PolicyEpoch + 1
            }), RootPrincipalId);
        await Assert.That(Errors.Code(revokeResult.Problem)).IsNull();

        var journalResult = Apply(database.Database, OperationKind.RuntimeJournal,
            JournalAuthorityTestBootstrap.CreateMutation(), RuntimeJournalIdentity.ProtectedPrincipalId);
        await Assert.That(Errors.Code(journalResult.Problem)).IsNull();
        await Assert.That(journalResult.Get<RuntimeJournalMutationResult>().Applied).IsTrue();

        var catalog = database.Database.ReadRuntimeJournalCatalog(RuntimeJournalIdentity.ProtectedPrincipalId);
        await Assert.That(catalog.Journals.Length).IsEqualTo(1);
        await Assert.That(catalog.Journals[0].JournalName).IsEqualTo("authorization-journal");
        await Assert.That(catalog.Journals[0].ContentRevision).IsEqualTo(0);
        await Assert.That(catalog.Journals[0].Length).IsEqualTo(0);
        var revokedRoot = database.Database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(RootPrincipalId)));
        await Assert.That(revokedRoot!.Revoked).IsTrue();
    }

    private static Result<object> Apply(DatabaseEngine database, OperationKind kind, object payload, string principalId)
        => database.Apply(database.CreateCommand(kind, Guid.NewGuid(), principalId, OperationTime, JsonSerializer.Serialize(payload, payload.GetType(), JsonDefaults.Options)));
}
