using System.Text.Json;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class McpScopeDeniedDiagnosticWholeFlowTests
{
    private const string Root = "root";
    private const string Reader = "mcp-denied-reader";
    private const string Collection = "mcp-denied-documents";
    private const string Document = "one";
    private const string OriginalJson = "{\"value\":\"private-diagnostic-canary\"}";
    private const string ScopeDenied = "The principal cannot perform this operation in this scope.";
    private const string GenericDenied = "The authenticated principal is not permitted to perform this operation.";
    private const long First = 1;
    private const long Second = 2;
    private const long Absent = 0;

    [Test]
    public async Task PersistedDenialBeforeTokenDiagnosticKeepsOwnedProblemThenFreshGrantLiteralRead()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, database.Partition,
            [new PutDocument(Collection, Document, OriginalJson, ExpectedRevision: Absent)]);
        var receipt = database.Database.Apply(new(id, OperationKind.Batch, Root,
            database.Database.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(command, JsonDefaults.Options)), First).Get<CommitReceipt>();
        var principal = new PrincipalRecord(Reader, database.Partition.TenantId, [], []);
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        var reference = new EntityRef(database.Partition, Collection, Document);
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.GetDocument(Reader,
            reference, receipt.Token with { Incarnation = Guid.NewGuid() }));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(denied.Message).IsEqualTo(ScopeDenied);
        await McpOwnedDiagnosticAssertions.ReplyAsync(denied.Code, denied.Message, ScopeDenied);
        await McpOwnedDiagnosticAssertions.ReplyAsync(denied.Code,
            denied.Message + McpOwnedDiagnosticAssertions.PrivateCanary, GenericDenied);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        var granted = principal with
        {
            PolicyEpoch = Second,
            Grants = [new(database.Partition.DatabaseId, Collection, Capability.DocumentsRead)]
        };
        var actualPrincipal = database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(granted)).Get<PrincipalRecord>();
        await Assert.That(NativeSerialization.Serialize(actualPrincipal).SequenceEqual(NativeSerialization.Serialize(granted))).IsTrue();
        var healthyState = QueueWholeFlowStorage.Bytes(database.Store);
        var healthyPosition = database.Store.Position;
        var healthy = database.Database.GetDocument(Reader, reference, receipt.Token);
        var expected = new DocumentResult(reference, First, OriginalJson, false, []);
        await Assert.That(NativeSerialization.Serialize(healthy).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(healthyState, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(healthyPosition);
    }
}
