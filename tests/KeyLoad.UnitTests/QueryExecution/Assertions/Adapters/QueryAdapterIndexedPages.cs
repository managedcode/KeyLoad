using System.Collections.Immutable;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class QueryAdapterIndexedPages
{
    private const string MissingPage = "The original indexed adapter page is absent.";

    internal static async Task<QueryPage> ReadAsync(ZoneTreeStore store, Func<QueryPage> read,
        ImmutableArray<QueryRow> expected, bool continues)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var position = store.Position;
        var identity = JsonDefaults.Serialize(store.Identity);
        var failures = new List<Exception>();
        QueryPage? actual = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            actual = read();
            await Assert.That(JsonDefaults.Serialize(actual.Rows)).IsEquivalentTo(JsonDefaults.Serialize(expected), CollectionOrdering.Matching);
            await Assert.That(actual.CutPosition).IsEqualTo(position);
            await Assert.That(actual.AccessPath).IsEqualTo(QueryAdapterIndexedCorpus.IndexPath);
            if (continues)
            {
                await Assert.That(actual.Cursor).IsNotNull();
            }
            else
            {
                await Assert.That(actual.Cursor).IsNull();
            }
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => SqlOffsetRefusals.UnchangedAsync(store, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return actual ?? throw new InvalidOperationException(MissingPage);
    }
}
