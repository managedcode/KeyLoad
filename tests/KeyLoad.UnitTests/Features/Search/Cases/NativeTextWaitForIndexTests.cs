using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextWaitForIndexTests
{
    private const long InitialSchemaVersion = 1;
    private const long InitialPolicyEpoch = 1;
    private const string Root = "root";
    private const string TextField = "/text";
    private const string WrongIncarnationDetail = "The index wait token belongs to another incarnation, atomic partition or placement.";
    private const string FutureDetail = "The index wait token is beyond the current quorum-applied cut.";
    private const long NextPosition = 1;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RejectedMinimumPrefixRetainsCompleteStoreThenNativePublicationReturnsLiteralHealthy(bool future)
    {
        await using var fixture = new ReplicaAppliedPositionWaitFixture();
        var database = fixture.Canonical;
        database.Configure(NativeTextBilingualAudit.Collection, ResourceKind.Collection);
        var token = TestContext.Current!.Execution.CancellationToken;
        var receipt = await NativeTextWaitSeed.CommitAsync(fixture, token);
        using var projection = NativeTextBilingualAudit.Open(database);
        var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
        var original = new WaitForIndexRequest(database.Partition, NativeTextBilingualAudit.Collection, TextField, receipt.Token);
        var invalid = future ? receipt.Token with { Position = receipt.Token.Position + NextPosition }
            : receipt.Token with { Incarnation = Guid.NewGuid() };
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => search.WaitForIndexAsync(Root,
            original with { MinimumToken = invalid }, token)) ?? throw new InvalidOperationException();
        await Assert.That(error.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(error.Message).IsEqualTo(future ? FutureDetail : WrongIncarnationDetail);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        var result = await search.WaitForIndexAsync(Root, original, token);
        await Assert.That(NativeSerialization.Serialize(result.AppliedToken).SequenceEqual(NativeSerialization.Serialize(receipt.Token))).IsTrue();
        await Assert.That(result.SchemaVersion).IsEqualTo(InitialSchemaVersion);
        await Assert.That(result.PolicyEpoch).IsEqualTo(InitialPolicyEpoch);
        await NativeTextBilingualAudit.VerifyAsync(search, database.Partition, token);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
