using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextWaitThrowingObservationTrial
{
    internal static async Task RunAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var failures = new List<Exception>();
        try
        {
            await using var fixture = new ReplicaAppliedPositionWaitFixture();
            NativeTextProjection? projection = null;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var database = fixture.Canonical;
                database.Configure(NativeTextBilingualAudit.Collection, ResourceKind.Collection);
                var receipt = await NativeTextWaitSeed.CommitAsync(fixture, token);
                projection = NativeTextBilingualAudit.Open(database);
                var originalFailure = new IOException("The actual post-read native wait observer failed.");
                var observer = new NativeTextWaitThrowingObservationObserver(database.Store, originalFailure);
                var observed = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection,
                    annProjection: null, readCutObserver: observer);
                var request = new WaitForIndexRequest(database.Partition, NativeTextBilingualAudit.Collection,
                    "/text", receipt.Token);
                var image = QueueWholeFlowStorage.Bytes(database.Store);
                var position = database.Store.Position;
                var failure = await Assert.ThrowsExactlyAsync<IOException>(() =>
                    observed.WaitForIndexAsync("root", request, token)) ?? throw new InvalidOperationException();
                await Assert.That(ReferenceEquals(failure, originalFailure)).IsTrue();
                await Assert.That(observer.Capture.Count).IsEqualTo(1);
                await Assert.That(observer.Capture.Image!).IsEquivalentTo(image, CollectionOrdering.Matching);
                await NativeTextWaitObservationAssertions.RequireImageAsync(database, image, position);
                var healthy = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
                var result = await healthy.WaitForIndexAsync("root", request, token);
                var expected = new WaitForIndexResult(receipt.Token, SchemaVersion: 1L, PolicyEpoch: 1L);
                await Assert.That(NativeSerialization.Serialize(result).SequenceEqual(
                    NativeSerialization.Serialize(expected))).IsTrue();
                await NativeTextWaitObservationAssertions.RequireAsync(database, observer.Capture, request, result,
                    "root", image, position);
                await NativeTextWaitObservationAssertions.RequirePagesAsync(database, healthy, "root", token);
                await NativeTextWaitObservationAssertions.RequireImageAsync(database, image, position);
                await Assert.That(observer.Capture.Count).IsEqualTo(1);
            }, failures);
            if (projection is not null)
            { ServerFailureObserver.Observe(projection.Dispose, failures); }
        }
        catch (Exception error) when (global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
