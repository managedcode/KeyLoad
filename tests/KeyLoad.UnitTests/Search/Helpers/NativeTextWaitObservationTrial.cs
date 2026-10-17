using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextWaitObservationTrial
{
    internal static async Task RunAsync(bool? future, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var failures = new List<Exception>();
        try
        {
            await using var fixture = new ReplicaAppliedPositionWaitFixture();
            await ExecuteAsync(fixture, future, failures, token);
        }
        catch (Exception failure) when (global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(ReplicaAppliedPositionWaitFixture fixture, bool? future,
        List<Exception> failures, CancellationToken token)
    {
        NativeTextProjection? projection = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var database = fixture.Canonical;
            database.Configure(NativeTextBilingualAudit.Collection, ResourceKind.Collection,
                fields: future is null ? [new("/text", "private")] : []);
            var receipt = await NativeTextWaitSeed.CommitAsync(fixture, token);
            projection = NativeTextBilingualAudit.Open(database);
            var observer = new NativeTextWaitObservationObserver(database.Store);
            var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection,
                annProjection: null, observer);
            var request = new WaitForIndexRequest(database.Partition, NativeTextBilingualAudit.Collection,
                "/text", receipt.Token);
            var principal = future is null ? NativeTextWaitObservationPermission.Reader : "root";
            if (future is null)
            {
                NativeTextWaitObservationPermission.Configure(database, allowField: false);
            }
            var image = QueueWholeFlowStorage.Bytes(database.Store);
            var position = database.Store.Position;
            var invalid = InvalidRequest(future, request, receipt.Token);
            var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
                search.WaitForIndexAsync(principal, invalid, token)) ?? throw new InvalidOperationException();
            await Assert.That(failure.Code).IsEqualTo(future is null ? ErrorCode.PermissionDenied : ErrorCode.TokenInvalidated);
            var expectedDetail = future is null ? "The query requires a protected field-use grant."
                : future.Value ? "The index wait token is beyond the current quorum-applied cut."
                : "The index wait token belongs to another incarnation, atomic partition or placement.";
            await Assert.That(failure.Message).IsEqualTo(expectedDetail);
            await Assert.That(observer.Count).IsEqualTo(0);
            await NativeTextWaitObservationAssertions.RequireImageAsync(database, image, position);
            if (future is null)
            {
                var allowed = NativeTextWaitObservationPermission.Configure(database, allowField: true);
                await Assert.That(allowed.PolicyEpoch).IsGreaterThan(1L);
                image = QueueWholeFlowStorage.Bytes(database.Store);
                position = database.Store.Position;
            }
            var result = await search.WaitForIndexAsync(principal, request, token);
            await Assert.That(result.SchemaVersion).IsEqualTo(1L);
            await Assert.That(result.PolicyEpoch).IsEqualTo(future is null ? 2L : 1L);
            await Assert.That(NativeSerialization.Serialize(result.AppliedToken).SequenceEqual(
                NativeSerialization.Serialize(receipt.Token))).IsTrue();
            await NativeTextWaitObservationAssertions.RequireAsync(database, observer, request, result,
                principal, image, position);
            var plain = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
            await NativeTextWaitObservationAssertions.RequirePagesAsync(database, plain, principal, token);
            await NativeTextWaitObservationAssertions.RequireImageAsync(database, image, position);
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
