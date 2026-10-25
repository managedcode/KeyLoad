using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextWaitForIndexTests
{
    private const long InitialSchemaVersion = 1;
    private const long InitialPolicyEpoch = 1;
    private const string Root = "root";
    private const string TextField = "/text";
    private const string WrongIncarnationDetail = "The index wait token belongs to another incarnation, atomic partition or placement.";
    private const string FutureDetail = "The index wait token is beyond the current committed storage cut.";
    private const long NextPosition = 1;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RejectedMinimumPrefixRetainsCompleteStoreThenPublicationReturnsLiteralHealthy(bool future)
    {
        using var fixture = new CanonicalReadCutFixture();
        var database = fixture.Canonical;
        database.Configure(TextBilingualAudit.Collection, ResourceKind.Collection);
        var token = TestContext.Current!.Execution.CancellationToken;
        var receipt = await TextWaitSeed.CommitAsync(fixture, token);
        using var projection = TextBilingualAudit.Open(database);
        var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
        var original = new WaitForIndexRequest(database.Partition, TextBilingualAudit.Collection, TextField, receipt.Token);
        var invalid = future ? receipt.Token with
        {
            Position = receipt.Token.Position + NextPosition
        }
            : receipt.Token with
            {
                Incarnation = Guid.NewGuid()
            };
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => search.WaitForIndexAsync(Root,
            original with
            {
                MinimumToken = invalid
            }, token)) ?? throw new InvalidOperationException();
        await Assert.That(error.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(error.Message).IsEqualTo(future ? FutureDetail : WrongIncarnationDetail);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        var result = await search.WaitForIndexAsync(Root, original, token);
        await Assert.That(OrleansSerialization.Serialize(result.AppliedToken).SequenceEqual(OrleansSerialization.Serialize(receipt.Token))).IsTrue();
        await Assert.That(result.SchemaVersion).IsEqualTo(InitialSchemaVersion);
        await Assert.That(result.PolicyEpoch).IsEqualTo(InitialPolicyEpoch);
        await TextBilingualAudit.VerifyAsync(search, database, token);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
