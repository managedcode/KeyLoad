using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextProjectionAuthorityTests
{
    private const string Collection = "native-text-authority";
    private const string TextPath = "/secret";
    private const string ReaderId = "reader";
    private const string DeniedId = "denied";
    private const string UseGrant = "secret.search";
    private const string ReadGrant = "secret.read";
    private const string VisibleId = "visible";
    private const string HiddenId = "hidden";

    [Test]
    public async Task PersistedFieldAndRowPolicyRemainAuthoritativeAcrossNativeReuseAndMutation()
    {
        using var database = new TestDatabase();
        ConfigureProtectedCollection(database);
        SeedProtectedRows(database);
        PersistReader(database, ReaderId, [UseGrant]);
        PersistReader(database, DeniedId, []);
        using var projection = new NativeTextProjection(Path.Combine(database.Directory, "native-text"), UnitExecutionOptions.DatabaseLimits(database.Database.Limits), database.Store.Identity.NodeId, UnitNativeTextOptions.Execution());
        var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
        var request = new SearchRequest(database.Partition, Collection, TextPath, "needle", Limit: 10);
        var token = TestContext.Current!.Execution.CancellationToken;

        var first = await Assert.That(await search.SearchAsync(ReaderId, request, token)).HasSingleItem();
        await Assert.That(first.Document.Reference.Id).IsEqualTo(VisibleId);
        await Assert.That(first.Document.Json).IsEqualTo("{\"other\":\"visible\"}");
        var firstGeneration = CurrentGeneration(database);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            search.Search(DeniedId, request, token)).Code)
            .IsEqualTo(ErrorCode.PermissionDenied);

        PatchVisibleSecret(database);
        var updated = await Assert.That(await search.SearchAsync(ReaderId,
            request with { Text = "changed" }, token)).HasSingleItem();
        await Assert.That(updated.Document.Reference.Id).IsEqualTo(VisibleId);
        await Assert.That(updated.Document.Json).IsEqualTo("{\"other\":\"visible\"}");
        var updatedGeneration = CurrentGeneration(database);
        await Assert.That(updatedGeneration).IsNotEqualTo(firstGeneration);

        database.Commit(new DeleteDocument(Collection, VisibleId, 2));
        var afterDelete = await search.SearchAsync(ReaderId, request with { Text = "changed" }, token);
        await Assert.That(afterDelete).IsEmpty();
        var deletedGeneration = CurrentGeneration(database);
        await Assert.That(deletedGeneration).IsNotEqualTo(updatedGeneration);

        AdvanceReaderPolicyEpoch(database);
        _ = await search.SearchAsync(ReaderId, request with { Text = "changed" }, token);
        var policyGeneration = CurrentGeneration(database);
        await Assert.That(policyGeneration).IsNotEqualTo(deletedGeneration);

        RevokeReader(database);
        var revokedImage = KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(database.Store);
        var revokedCut = database.Store.Position;
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            search.Search(ReaderId, request with { Text = "changed" }, token)).Code)
            .IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(database.Store.Position).IsGreaterThan(0L);
        await NativeTextAuthorityContinuation.RunAsync(database, projection, search, request,
            revokedImage, revokedCut, () => UpdateReader(database, principal => principal with { Revoked = false }), token);
    }

    private static void ConfigureProtectedCollection(TestDatabase database)
        => database.Configure(Collection, ResourceKind.Collection, fields:
            [new(TextPath, "secret", ReadGrant, UseGrant, "secret.write")]);

    private static void SeedProtectedRows(TestDatabase database)
        => database.Commit(
            new PutDocument(Collection, VisibleId, "{\"secret\":\"needle\",\"other\":\"visible\"}",
                Access: new(ReaderId)),
            new PutDocument(Collection, HiddenId, "{\"secret\":\"needle\",\"other\":\"hidden\"}",
                Access: new("another-owner")));

    private static void PersistReader(TestDatabase database, string principalId, string[] fieldGrants)
    {
        var reader = new PrincipalRecord(principalId, database.Partition.TenantId,
            [new("database", Collection, Capability.Query | Capability.DocumentsRead)], [.. fieldGrants])
        { OwnerId = ReaderId, RestrictRows = true };
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(reader)).Get<PrincipalRecord>();
    }

    private static void PatchVisibleSecret(TestDatabase database)
        => database.Commit(new PatchDocument(Collection, VisibleId,
            [new(TextPath, PatchKind.Set, "\"changed\"")], 1));

    private static void RevokeReader(TestDatabase database)
        => UpdateReader(database, principal => principal with { Revoked = true });

    private static void AdvanceReaderPolicyEpoch(TestDatabase database)
        => UpdateReader(database, principal => principal);

    private static void UpdateReader(TestDatabase database, Func<PrincipalRecord, PrincipalRecord> update)
    {
        var principal = database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(ReaderId)))!;
        database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(update(principal) with { PolicyEpoch = principal.PolicyEpoch + 1 }))
            .Get<PrincipalRecord>();
    }

    private static string CurrentGeneration(TestDatabase database)
        => Directory.EnumerateDirectories(Path.Combine(database.Directory, "native-text"))
            .Single(path => Path.GetFileName(path).StartsWith(NativeTextProtocol.GenerationPrefix,
                StringComparison.Ordinal));
}
