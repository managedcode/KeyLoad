using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

/// <summary>Owns actual replica-applied seed, precisely damaged native authority and failure-preserving original cleanup.</summary>
internal static class SearchWaitAuthorityTrial
{
    private const string TokenDetail = "The index wait token belongs to another incarnation, atomic partition or placement.";
    private const string AppliedDetail = "The canonical applied position is invalid.";
    private const string ProviderDetail = "Native text index waiting is unavailable.";
    private const long NegativeAppliedPosition = -1;

    internal static Task TokenAsync(Func<CommitToken, CommitToken> damage, CancellationToken cancellationToken)
        => RunAsync(async (fixture, search, request) =>
        {
            await SearchWaitAuthorityState.DeniedAsync(fixture.Canonical, search,
                request with { MinimumToken = damage(request.MinimumToken) }, ErrorCode.TokenInvalidated, TokenDetail, cancellationToken);
            await SearchWaitAuthorityState.HealthyAsync(fixture.Canonical, search, request, cancellationToken);
        }, cancellationToken);

    internal static Task ProviderAsync(CancellationToken cancellationToken)
        => RunAsync(async (fixture, search, request) =>
        {
            var unavailable = new SearchEngine(fixture.Canonical.Database, UnitExecutionOptions.QueryExecution());
            await SearchWaitAuthorityState.DeniedAsync(fixture.Canonical, unavailable, request,
                ErrorCode.UnsupportedCapability, ProviderDetail, cancellationToken);
            await SearchWaitAuthorityState.HealthyAsync(fixture.Canonical, search, request, cancellationToken);
        }, cancellationToken);

    internal static Task AppliedAsync(bool missing, CancellationToken cancellationToken)
        => RunAsync(async (fixture, search, request) =>
        {
            var database = fixture.Canonical;
            var original = database.Store.Read(view => view.ReadOwnedValue(KeySpace.AppliedBytes))
                ?? throw new InvalidOperationException();
            database.Store.Commit((transaction, _) =>
            {
                if (missing)
                { transaction.Delete(KeySpace.AppliedBytes); }
                else
                { transaction.Put(KeySpace.AppliedBytes, NativeSerialization.Serialize(NegativeAppliedPosition)); }
                return true;
            });
            var failures = new List<Exception>();
            try
            {
                await ServerFailureObserver.ObserveAsync(() => SearchWaitAuthorityState.DeniedAsync(database,
                    search, request, ErrorCode.Corruption, AppliedDetail, cancellationToken), failures);
            }
            finally
            {
                ServerFailureObserver.Observe(() => database.Store.Commit((transaction, _) =>
                { transaction.Put(KeySpace.AppliedBytes, original); return true; }), failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            await SearchWaitAuthorityState.HealthyAsync(database, search, request, cancellationToken);
        }, cancellationToken);

    private static async Task RunAsync(Func<ReplicaAppliedPositionWaitFixture, SearchEngine, WaitForIndexRequest, Task> operation,
        CancellationToken cancellationToken)
    {
        ReplicaAppliedPositionWaitFixture? fixture = null;
        NativeTextProjection? projection = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                fixture = new ReplicaAppliedPositionWaitFixture();
                var database = fixture.Canonical;
                database.Configure(NativeTextBilingualAudit.Collection, ResourceKind.Collection);
                var receipt = await NativeTextWaitSeed.CommitAsync(fixture, cancellationToken);
                projection = NativeTextBilingualAudit.Open(database);
                var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
                var request = new WaitForIndexRequest(database.Partition, NativeTextBilingualAudit.Collection,
                    SearchWaitAuthorityState.TextField, receipt.Token);
                await operation(fixture, search, request);
            }, failures);
        }
        finally
        {
            if (projection is not null)
            { ServerFailureObserver.Observe(projection.Dispose, failures); }
            if (fixture is not null)
            { await ServerFailureObserver.ObserveAsync(() => fixture.DisposeAsync().AsTask(), failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
