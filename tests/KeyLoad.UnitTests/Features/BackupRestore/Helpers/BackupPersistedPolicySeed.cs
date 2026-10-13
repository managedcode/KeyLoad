using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.BackupRestore;

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
        { PolicyEpoch = repaired ? 3L : 2L };

    internal static ResourceDefinition Resource(PartitionRef partition)
        => new(Collection, ResourceKind.Collection, partition.TransactionDomainId)
        { FieldPolicies = [new("/secret", "private")] };

    internal static ApiKeyRecord LiveKey => DatabaseEngine.Credential(LiveKeyId, Reader, LiveSecret);
    internal static ApiKeyRecord RevokedKey => DatabaseEngine.Credential(RevokedKeyId, Reader, RevokedSecret) with { Revoked = true };

    internal static ReplicatedOperation Create<T>(DatabaseEngine database, OperationKind kind, T request, Guid id)
        => database.CreateNativeOperation(kind, id, Root, database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(request));

    internal static OperationResult Send<T>(DatabaseEngine database, OperationKind kind, T request, CancellationToken token)
        => database.ApplyEmbedded(Create(database, kind, request, Guid.NewGuid()), token);

    internal static async Task<(ReplicatedOperation Operation, OperationResult Result)> RunAsync(
        BackupIndexedDocumentDedupFixture fixture, CancellationToken token)
    {
        var database = fixture.Source.Database;
        var resource = Resource(fixture.Partition);
        await Assert.That(Send(database, OperationKind.ConfigureResource,
            new ConfigureResourceRequest(fixture.Partition.TenantId, fixture.Partition.DatabaseId, resource), token).Error).IsNull();
        await Assert.That(Send(database, OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(Principal(fixture.Partition, false)), token).Error).IsNull();
        await Assert.That(Send(database, OperationKind.ConfigureApiKey, new ConfigureApiKeyRequest(LiveKey), token).Get<bool>()).IsTrue();
        await Assert.That(Send(database, OperationKind.ConfigureApiKey, new ConfigureApiKeyRequest(RevokedKey), token).Get<bool>()).IsTrue();
        var id = Guid.NewGuid();
        var operation = Create(database, OperationKind.Batch, new CommandRequest(id, fixture.Partition,
            [new PutDocument(Collection, DocumentId, Json)]), id);
        var result = database.ApplyEmbedded(operation, token);
        await Assert.That(result.Error).IsNull();
        await BackupPersistedPolicyAssertions.ReceiptAsync(database, fixture.Partition, id, result.Get<CommitReceipt>());
        await BackupPersistedPolicyAssertions.CurrentAsync(fixture, database, false, token);
        await BackupPersistedPolicyAssertions.ReplayAsync(database, operation, result, token);
        return (operation, result);
    }
}
