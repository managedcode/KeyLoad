using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal static class BackupPersistedPolicySeed
{
    internal const string Root = "root";
    internal const string Reader = "backup-policy-reader";
    internal const string Collection = "backup_policy_documents";
    internal const string DocumentId = "protected";
    internal const string HealthyId = "healthy";
    internal const string HealthyJson = "{\"status\":\"healthy\"}";
    internal const string Json = "{\"secret\":\"CANARY\",\"status\":\"open\"}";
    internal const string LiveSecret = "backup-live.unit-test-credential-32-characters";
    internal const string RevokedSecret = "backup-revoked.unit-test-credential-32-characters";
    internal const string LiveKeyId = "backup-live";
    internal const string RevokedKeyId = "backup-revoked";

    internal static PrincipalRecord Principal(PartitionRef partition, bool repaired)
        => new(Reader, partition.TenantId,
            [new(partition.DatabaseId, Collection, Capability.DocumentsRead | Capability.Query)],
            repaired ? ["pii.read", "pii.use"] : ["pii.read"])
        {
            PolicyEpoch = repaired ? 3L : 2L
        };

    internal static ResourceDefinition Resource(PartitionRef partition)
        => new(Collection, ResourceKind.Collection, partition.TransactionDomainId)
        {
            FieldPolicies = [new("/secret", "private")]
        };

    internal static ApiKeyRecord LiveKey => AuthorizationExecution.Credential(LiveKeyId, Reader, LiveSecret);
    internal static ApiKeyRecord RevokedKey => AuthorizationExecution.Credential(RevokedKeyId, Reader, RevokedSecret) with { Revoked = true };

    internal static Command<DatabaseCommand> Create<T>(DatabaseEngine database, OperationKind kind, T request, Guid id) where T : notnull
        => database.CreateCommand(kind, id, Root, database.EvaluationClock.GetUtcNow(),
            request);

    internal static Result<object> Send<T>(DatabaseEngine database, OperationKind kind, T request, CancellationToken token)
        where T : notnull => database.ApplyEmbedded(Create(database, kind, request, Guid.NewGuid()), token);

    internal static async Task<(Command<DatabaseCommand> Operation, Result<object> Result)> RunAsync(
        BackupIndexedDocumentDedupFixture fixture, CancellationToken token)
    {
        var database = fixture.Source.Database;
        var resource = Resource(fixture.Partition);
        var creation = Create(database, OperationKind.ConfigureResource,
            new ConfigureResourceRequest(fixture.Partition.TenantId, fixture.Partition.DatabaseId, resource), Guid.NewGuid());
        await Assert.That(Errors.Code(fixture.Source.SubmitIssuedEmbedded(creation, token).Problem).Value).IsNull();
        await Assert.That(Errors.Code(Send(database, OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(Principal(fixture.Partition, false)), token).Problem).Value).IsNull();
        await Assert.That(Send(database, OperationKind.ConfigureApiKey, new ConfigureApiKeyRequest(LiveKey), token).Get<bool>()).IsTrue();
        await Assert.That(Send(database, OperationKind.ConfigureApiKey, new ConfigureApiKeyRequest(RevokedKey), token).Get<bool>()).IsTrue();
        var id = Guid.NewGuid();
        var operation = Create(database, OperationKind.Batch, new CommandRequest(id, fixture.Partition,
            [new PutDocument(Collection, DocumentId, Json)]), id);
        var result = database.ApplyEmbedded(operation, token);
        await Assert.That(Errors.Code(result.Problem).Value).IsNull();
        await BackupPersistedPolicyAssertions.ReceiptAsync(database, fixture.Partition, id, result.Get<CommitReceipt>());
        await BackupPersistedPolicyAssertions.CurrentAsync(fixture, database, false, token);
        await BackupPersistedPolicyAssertions.ReplayAsync(database, operation, result, token);
        return (operation, result);
    }
}
