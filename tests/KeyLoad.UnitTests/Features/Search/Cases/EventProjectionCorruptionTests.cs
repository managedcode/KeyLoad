using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class EventProjectionCorruptionTests
{
    [Test]
    public async Task AcLineage002MalformedPersistedLineageFailsClosedInsteadOfLeakingTheVector()
    {
        using var fixture = new EventProjectionFixture();
        fixture.Apply(fixture.Request());
        var lineage = fixture.Lineage()!;
        fixture.Harness.Store.Commit((transaction, _) =>
        {
            transaction.PutRecord(fixture.LineageKey(), lineage with { SourceEventRevision = 0 });
            return true;
        });

        var error = (await Assert.ThrowsExactlyAsync<KeyLoadException>(fixture.SearchAsync))!;

        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
    }
}
