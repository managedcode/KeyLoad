namespace KeyLoad.UnitTests.ResourceExecution;

internal sealed class TransactionAuthorizationTests
{
    [Test]
    public async Task InvalidCatalogArraysGrantsAndVerifiersCannotPoisonReplicatedAuthorization()
    {
        using var db = new TestDatabase();
        var definition = new ResourceDefinition("invalid", ResourceKind.Collection, db.Partition.TransactionDomainId);
        foreach (var invalid in new[] { definition with { Indexes = [null!] }, definition with { FieldPolicies = [null!] },
            definition with { HeaderPolicies = [null!] }, definition with { Indexes = [new("field", [null!])] }, definition with { Kind = (ResourceKind)99 } })
        {
            await Assert.That(Errors.Code(db.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest("tenant", "database", invalid)).Problem)).IsEqualTo(ErrorCode.Validation);
        }

        foreach (var member in new[] { "fieldPolicies", "headerPolicies" })
        {
            var payload = "{\"tenantId\":\"tenant\",\"databaseId\":\"database\",\"definition\":{\"name\":\"invalid\",\"kind\":\"collection\",\"transactionDomainId\":\"orders\",\""
                + member + "\":[{\"path\":null,\"classification\":\"invalid\"}]}}";
            var before = db.ReadCanonicalCut();
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.CreateCommand(
                OperationKind.ConfigureResource, Guid.NewGuid(), "root", default, payload));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(db.ReadCanonicalCut()).IsEqualTo(before);
        }

        foreach (var grants in new ScopeGrant[][] { [null!], [new("database", "orders", (Capability)(1L << 50))] })
        {
            await Assert.That(Errors.Code(db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("invalid", "tenant", [.. grants], []))).Problem)).IsEqualTo(ErrorCode.Validation);
        }

        await Assert.That(Errors.Code(db.Submit(OperationKind.ConfigureApiKey, new ConfigureApiKeyRequest(new("invalid", "root", "invalid-verifier"))).Problem)).IsEqualTo(ErrorCode.Validation);
        await Assert.That(db.Database.Authenticate("root.unit-test-credential-32-characters", TimeProvider.System.GetUtcNow())).IsEqualTo("root");
        db.Configure("orders", ResourceKind.Collection);
        db.Commit(new PutDocument("orders", "valid", "{}", 0));
    }
    [Test]
    public async Task AShadowedMutationResourceCannotAuthorizeWritesToAnotherCollection()
    {
        using var db = new TestDatabase();
        db.Configure("orders", ResourceKind.Collection);
        db.Configure("other", ResourceKind.Collection);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("writer", "tenant", [new("database", "other", Capability.DocumentsWrite)], []))).Get<PrincipalRecord>();
        var id = Guid.NewGuid();
        var attack = new PutDocument("orders", "unauthorized", "{}", 0) { Resource = "other" };
        await Assert.That(Errors.Code(db.Submit(OperationKind.Batch, new CommandRequest(id, db.Partition, [attack]), "writer", id).Problem)).IsEqualTo(ErrorCode.Validation);
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "orders", "unauthorized"))).IsNull();
        var allowedId = Guid.NewGuid();
        db.Submit(OperationKind.Batch, new CommandRequest(allowedId, db.Partition, [new PutDocument("other", "allowed", "{}", 0)]), "writer", allowedId).Get<CommitReceipt>();
        await Assert.That(db.Database.GetDocument("root", new(db.Partition, "other", "allowed"))).IsNotNull();
    }
}
