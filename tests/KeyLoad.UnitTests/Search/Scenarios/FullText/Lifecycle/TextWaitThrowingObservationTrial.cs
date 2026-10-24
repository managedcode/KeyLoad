using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Search;
using KeyLoad.UnitTests.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Search;

internal static class TextWaitThrowingObservationTrial
{
    internal static async Task RunAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var failures = new List<Exception>();
        try
        {
            using var fixture = new CanonicalReadCutFixture();
            RuntimeTextProjection? projection = null;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var database = fixture.Canonical;
                database.Configure(TextBilingualAudit.Collection, ResourceKind.Collection);
                var receipt = await TextWaitSeed.CommitAsync(fixture, token);
                projection = TextBilingualAudit.Open(database);
                var originalFailure = new IOException("The actual post-read execution wait observer failed.");
                var observer = new TextWaitThrowingObservationObserver(database.Store, originalFailure);
                var observed = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection,
                    annProjection: null, readCutObserver: observer);
                var request = new WaitForIndexRequest(database.Partition, TextBilingualAudit.Collection,
                    "/text", receipt.Token);
                var image = QueueWholeFlowStorage.Bytes(database.Store);
                var position = database.Store.Position;
                var failure = await Assert.ThrowsExactlyAsync<IOException>(() =>
                    observed.WaitForIndexAsync("root", request, token)) ?? throw new InvalidOperationException();
                await Assert.That(ReferenceEquals(failure, originalFailure)).IsTrue();
                await Assert.That(observer.Capture.Count).IsEqualTo(1);
                await Assert.That(observer.Capture.Image!).IsEquivalentTo(image, CollectionOrdering.Matching);
                await TextWaitObservationAssertions.RequireImageAsync(database, image, position);
                var healthy = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
                var result = await healthy.WaitForIndexAsync("root", request, token);
                var expected = new WaitForIndexResult(receipt.Token, SchemaVersion: 1L, PolicyEpoch: 1L);
                await Assert.That(OrleansSerialization.Serialize(result).SequenceEqual(
                    OrleansSerialization.Serialize(expected))).IsTrue();
                await TextWaitObservationAssertions.RequireAsync(database, observer.Capture, request, result,
                    "root", image, position);
                await TextWaitObservationAssertions.RequirePagesAsync(database, healthy, "root", token);
                await TextWaitObservationAssertions.RequireImageAsync(database, image, position);
                await Assert.That(observer.Capture.Count).IsEqualTo(1);
            }, failures);
            if (projection is not null)
            {
                ServerFailureObserver.Observe(projection.Dispose, failures);
            }
        }
        catch (Exception error) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
