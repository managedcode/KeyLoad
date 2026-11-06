using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextWaitObservedWorkTests
{
    private const long InitialSchemaVersion = 1;
    private const long InitialPolicyEpoch = 1;
    private const string Root = "root";
    private const string TextField = "/text";

    [Test]
    public async Task ActualPublicationWorkCancellationSettlesWithoutPartialThenLiteralHealthy()
    {
        using var fixture = new CanonicalReadCutFixture(
            new DatabaseLimits());
        var database = fixture.Canonical;
        database.Configure(TextBilingualAudit.Collection, ResourceKind.Collection);
        var receipt = await TextWaitSeed.CommitAsync(fixture, TestContext.Current!.Execution.CancellationToken);
        using var projection = TextBilingualAudit.Open(database);
        var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
        var request = new WaitForIndexRequest(database.Partition, TextBilingualAudit.Collection, TextField, receipt.Token);
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var cut = database.Store.Position;
        var before = database.Store.GetReadDiagnostics();
        using var cancellation = new CancellationTokenSource();
        await using var observed = new ObservedWorkCancellation(database.Store, cancellation,
            static progress => progress.RangeExaminedBytes);
        await observed.Ready;
        WaitForIndexResult? partial = null;
        var failure = await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            {
                partial = await search.WaitForIndexAsync(Root, request, cancellation.Token);
            }) ?? throw new InvalidOperationException();
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(observed.CancelledAfterProgress).IsTrue();
        await Assert.That(partial).IsNull();
        await Assert.That(database.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        var healthy = await search.WaitForIndexAsync(Root, request, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(OrleansSerialization.Serialize(healthy.AppliedToken).SequenceEqual(OrleansSerialization.Serialize(receipt.Token))).IsTrue();
        await Assert.That(healthy.SchemaVersion).IsEqualTo(InitialSchemaVersion);
        await Assert.That(healthy.PolicyEpoch).IsEqualTo(InitialPolicyEpoch);
        await TextBilingualAudit.VerifyAsync(search, database, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(database.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
