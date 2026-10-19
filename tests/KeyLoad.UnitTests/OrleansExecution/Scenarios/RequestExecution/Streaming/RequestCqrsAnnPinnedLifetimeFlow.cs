using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.Search;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class RequestCqrsAnnPinnedLifetimeFlow
{
    private const int SingleRecord = 1;
    private const int FirstOrdinal = 0;
    private const ulong EligibleFirst = 1;
    private const double OrthogonalScore = 0;

    internal static async Task RunAsync(RequestCqrsHostedRuntimeFixture fixture,
        AnnMaintenanceRequest current, string leaf, CancellationToken token)
    {
        var original = Receipt(fixture).OriginalOperations.Single(row => row.Key == new AnnOwnedKey(current.Consumer, current.IndexGeneration));
        var leafFiles = AnnFixtureFileSnapshot.Capture(fixture.Database.Directory)
            .Where(row => Path.GetDirectoryName(row.Path) == leaf).ToArray();
        await Assert.That(leafFiles).IsNotEmpty();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var held = Acquire(fixture, current, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await ReleaseAsync(fixture, current, token);
                await RetainedAsync(fixture, current, original, leaf, leafFiles, held, token);
                await RefuseFreshReadAsync(fixture, current, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static IAnnProjectionLease Acquire(RequestCqrsHostedRuntimeFixture fixture,
        AnnMaintenanceRequest request, CancellationToken token)
    {
        var database = fixture.Database.Database;
        var runtime = fixture.RuntimeAnnMaintenance ?? throw new InvalidOperationException();
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, token);
        return database.WithQueryView(RequestCqrsAnnTestData.Principal, request.Consumer.Partition, request.Collection,
            (view, principal, resource) =>
            {
                database.Authorization.Require(principal, request.Consumer.Partition, request.Collection, Capability.VectorSearch);
                database.Authorization.RequireFieldUse(principal, resource, request.Field);
                return runtime.Acquire(view, new(request.Consumer.Partition, request.Collection, request.Field,
                    request.Space, request.Consumer, request.IndexGeneration), budget);
            });
    }

    private static async Task ReleaseAsync(RequestCqrsHostedRuntimeFixture fixture,
        AnnMaintenanceRequest current, CancellationToken token)
    {
        var release = current with
        {
            CommandId = Guid.NewGuid(),
            Mode = AnnMaintenanceMode.Release
        };
        var released = await RequestCqrsAnnInvocation.RunAsync(fixture, release, token);
        await RequestCqrsAnnAssertions.EqualAsync(new AnnMaintenanceResult(release.CommandId, release.Consumer,
            release.IndexGeneration, AnnMaintenancePhase.Completed, null, RequestCqrsAnnTestData.EmptyCount, null, null), released.Result);
    }

    private static async Task RetainedAsync(RequestCqrsHostedRuntimeFixture fixture, AnnMaintenanceRequest current,
        AnnOriginalOperation original, string leaf, (string Path, string Bytes)[] leafFiles,
        IAnnProjectionLease held, CancellationToken token)
    {
        var receipt = Receipt(fixture);
        await Assert.That(receipt.Published.Any(row => row.Key == original.Key)).IsFalse();
        await Assert.That(receipt.Pending.Any(row => row.Key == original.Key)).IsFalse();
        await RequestCqrsAnnAssertions.EqualAsync(original with
        {
            Released = true
        },
            receipt.OriginalOperations.Single(row => row.Key == original.Key));
        await Assert.That(Directory.Exists(Path.Combine(fixture.Database.Directory, AnnProtocol.RootDirectory, leaf))).IsTrue();
        await Assert.That(AnnFixtureFileSnapshot.Capture(fixture.Database.Directory)
            .Where(row => Path.GetDirectoryName(row.Path) == leaf).ToArray())
            .IsEquivalentTo(leafFiles, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var image = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        var cut = fixture.Database.Store.Position;
        var files = AnnFixtureFileSnapshot.Capture(fixture.Database.Directory);
        await Assert.That(held.IndexGeneration).IsEqualTo(current.IndexGeneration);
        await Assert.That(held.Records.Length).IsEqualTo(SingleRecord);
        await RequestCqrsAnnAssertions.EqualAsync(new VectorRecord(RequestCqrsAnnTestData.First,
            RequestCqrsAnnTestData.Field, RequestCqrsAnnTestData.Space, [0, 0, 1], RequestCqrsAnnTestData.ChangedRevision),
            held.Records[FirstOrdinal]);
        var database = fixture.Database.Database;
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, database.EvaluationClock, token);
        var actual = held.Search(RequestCqrsAnnTestData.Query, SingleRecord, new ulong[] { EligibleFirst },
            new AnnWorkBudget(budget, held.CallerSeedOptions.Value.MaxWorkUnits));
        await Assert.That(actual.Mode).IsEqualTo(AnnSearchMode.ExactSmallSet);
        await Assert.That(actual.EligibleCount).IsEqualTo(SingleRecord);
        await Assert.That(actual.Candidates.Length).IsEqualTo(SingleRecord);
        var candidate = actual.Candidates[FirstOrdinal];
        await Assert.That(candidate.SourceOrdinal).IsEqualTo(FirstOrdinal);
        await Assert.That(candidate.DocumentId).IsEqualTo(RequestCqrsAnnTestData.First);
        await Assert.That(candidate.DocumentRevision).IsEqualTo(RequestCqrsAnnTestData.ChangedRevision);
        await Assert.That(candidate.Score).IsEqualTo(OrthogonalScore);
        await UnchangedAsync(fixture, image, cut, files);
    }

    private static async Task RefuseFreshReadAsync(RequestCqrsHostedRuntimeFixture fixture,
        AnnMaintenanceRequest request, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        var cut = fixture.Database.Store.Position;
        var files = AnnFixtureFileSnapshot.Capture(fixture.Database.Directory);
        var execution = fixture.RuntimeAnnMaintenance ?? throw new InvalidOperationException();
        var search = new SearchEngine(fixture.Database.Database,
            Microsoft.Extensions.Options.Options.Create(new QueryExecutionOptions { EnableApproximateSearch = true }), null, execution);
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => search.ApproximateSearchAsync(
            RequestCqrsAnnTestData.Principal, new(RequestCqrsAnnTestData.Version,
                new(fixture.Database.Partition, request.Collection, VectorField: request.Field,
                    Vector: [.. RequestCqrsAnnTestData.Query], Space: request.Space, Limit: SingleRecord),
                request.Consumer, request.IndexGeneration), token)) ?? throw new InvalidOperationException();
        await Assert.That(error.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        await UnchangedAsync(fixture, image, cut, files);
    }

    private static AnnRootReceipt Receipt(RequestCqrsHostedRuntimeFixture fixture)
        => AnnReceiptFiles.Read<AnnRootReceipt>(Path.Combine(fixture.Database.Directory,
            AnnProtocol.RootDirectory, AnnProtocol.RootReceipt), new AnnExecutionOptions()).Value;

    private static async Task UnchangedAsync(RequestCqrsHostedRuntimeFixture fixture, string[] image, long cut,
        (string Path, string Bytes)[] files)
    {
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Database.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(AnnFixtureFileSnapshot.Capture(fixture.Database.Directory))
            .IsEquivalentTo(files, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
