using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Search;

internal static class TextWaitObservationTrial
{
    internal static async Task RunAsync(bool? future, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var failures = new List<Exception>();
        try
        {
            using var fixture = new CanonicalReadCutFixture();
            await ExecuteAsync(fixture, future, failures, token);
        }
        catch (Exception failure) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(CanonicalReadCutFixture fixture, bool? future,
        List<Exception> failures, CancellationToken token)
    {
        RuntimeTextProjection? projection = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var database = fixture.Canonical;
            database.Configure(TextBilingualAudit.Collection, ResourceKind.Collection,
                fields: future is null ? [new("/text", "private")] : []);
            var receipt = await TextWaitSeed.CommitAsync(fixture, token);
            projection = TextBilingualAudit.Open(database);
            var observer = new TextWaitObservationObserver(database.Store);
            var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection,
                annProjection: null, observer);
            var request = new WaitForIndexRequest(database.Partition, TextBilingualAudit.Collection,
                "/text", receipt.Token);
            var principal = future is null ? TextWaitObservationPermission.Reader : "root";
            if (future is null)
            {
                TextWaitObservationPermission.Configure(database, allowField: false);
            }
            var image = QueueWholeFlowStorage.Bytes(database.Store);
            var position = database.Store.Position;
            var invalid = InvalidRequest(future, request, receipt.Token);
            var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
                search.WaitForIndexAsync(principal, invalid, token)) ?? throw new InvalidOperationException();
            await Assert.That(failure.Code).IsEqualTo(future is null ? ErrorCode.PermissionDenied : ErrorCode.TokenInvalidated);
            var expectedDetail = future is null ? "The query requires a protected field-use grant."
                : future.Value ? "The index wait token is beyond the current committed storage cut."
                : "The index wait token belongs to another incarnation, atomic partition or placement.";
            await Assert.That(failure.Message).IsEqualTo(expectedDetail);
            await Assert.That(observer.Count).IsEqualTo(0);
            await TextWaitObservationAssertions.RequireImageAsync(database, image, position);
            if (future is null)
            {
                var allowed = TextWaitObservationPermission.Configure(database, allowField: true);
                await Assert.That(allowed.PolicyEpoch).IsGreaterThan(1L);
                image = QueueWholeFlowStorage.Bytes(database.Store);
                position = database.Store.Position;
            }
            var expected = new CommitToken(database.Store.Identity.Incarnation, database.Partition.AtomicPartitionId,
                database.Database.LastApplied);
            var result = await search.WaitForIndexAsync(principal, request, token);
            await Assert.That(result.SchemaVersion).IsEqualTo(1L);
            await Assert.That(result.PolicyEpoch).IsEqualTo(future is null ? 2L : 1L);
            await Assert.That(OrleansSerialization.Serialize(result.AppliedToken).SequenceEqual(
                OrleansSerialization.Serialize(expected))).IsTrue();
            await TextWaitObservationAssertions.RequireAsync(database, observer, request, result,
                principal, image, position);
            var plain = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
            await TextWaitObservationAssertions.RequirePagesAsync(database, plain, principal, token);
            await TextWaitObservationAssertions.RequireImageAsync(database, image, position);
        }, failures);
        if (projection is not null)
        {
            ServerFailureObserver.Observe(projection.Dispose, failures);
        }
    }

    private static WaitForIndexRequest InvalidRequest(bool? future, WaitForIndexRequest request, CommitToken original)
    {
        var invalid = future is null ? request : request with
        {
            MinimumToken = future.Value
            ? original with
            {
                Position = checked(original.Position + 1L)
            }
            : original with
            {
                Incarnation = Guid.NewGuid()
            }
        };
        return invalid;
    }
}
