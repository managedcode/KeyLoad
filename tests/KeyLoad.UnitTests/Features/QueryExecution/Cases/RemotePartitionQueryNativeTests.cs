using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class RemotePartitionQueryNativeTests
{
    private const long UpdatedResourceVersion = 2;
    private const string UpdatedClassification = "changed-private";
    private const string FenceChangedDetail = "The remote partition query authority changed.";
    private const int CompleteLeafCount = 2;
    private const long RevokedSourceEpoch = 4;
    private const long RestoredSourceEpoch = 5;
    private const string DeniedDetail = "The principal cannot perform this operation in this scope.";
    private const string MissingFailure = "The native rejected query did not retain its failure.";

    [Test]
    public async Task ReceivingGrantDenialHasNoPartialPageThenIndependentOwnerPoliciesMergeLiteralRows()
        => await RemoteDocumentNativeAssertions.RunAsync(async fixture =>
        {
            var flow = new RemotePartitionQueryNativeFlow(fixture);
            flow.Seed();
            var source = RemoteDocumentNativeAssertions.Image(fixture.Source);
            var destination = RemoteDocumentNativeAssertions.Image(fixture.Destination);
            var sourcePosition = fixture.Source.Store.Position;
            var destinationPosition = fixture.Destination.Store.Position;
            PartitionQueryPageV1? page = null;
            var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () =>
                page = await flow.ExecuteAsync(CancellationToken.None)) ?? throw new InvalidOperationException(MissingFailure);
            await Assert.That(error.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(error.Message).IsEqualTo(DeniedDetail);
            await Assert.That(page).IsNull();
            await RemotePartitionQueryNativeAssertions.StateAsync(fixture, source, destination,
                sourcePosition, destinationPosition);
            flow.GrantDestination();
            source = RemoteDocumentNativeAssertions.Image(fixture.Source);
            destination = RemoteDocumentNativeAssertions.Image(fixture.Destination);
            destinationPosition = fixture.Destination.Store.Position;
            await RemotePartitionQueryNativeAssertions.PageAsync(await flow.ExecuteAsync(CancellationToken.None), fixture);
            await RemotePartitionQueryNativeAssertions.StateAsync(fixture, source, destination,
                sourcePosition, destinationPosition);
        });

    [Test]
    public async Task SourceGrantRevocationRejectsBeforeOwnerReadThenFreshGrantReturnsLiteralMerge()
        => await RemoteDocumentNativeAssertions.RunAsync(async fixture =>
        {
            var flow = new RemotePartitionQueryNativeFlow(fixture);
            flow.Seed();
            flow.GrantDestination();
            flow.SetSourceGrant(RevokedSourceEpoch, granted: false);
            var source = RemoteDocumentNativeAssertions.Image(fixture.Source);
            var destination = RemoteDocumentNativeAssertions.Image(fixture.Destination);
            var sourcePosition = fixture.Source.Store.Position;
            var destinationPosition = fixture.Destination.Store.Position;
            PartitionQueryPageV1? page = null;
            var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () =>
                page = await flow.ExecuteAsync(CancellationToken.None)) ?? throw new InvalidOperationException(MissingFailure);
            await Assert.That(error.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(error.Message).IsEqualTo(DeniedDetail);
            await Assert.That(page).IsNull();
            await RemotePartitionQueryNativeAssertions.StateAsync(fixture, source, destination,
                sourcePosition, destinationPosition);
            flow.SetSourceGrant(RestoredSourceEpoch, granted: true);
            source = RemoteDocumentNativeAssertions.Image(fixture.Source);
            sourcePosition = fixture.Source.Store.Position;
            await RemotePartitionQueryNativeAssertions.PageAsync(await flow.ExecuteAsync(CancellationToken.None),
                fixture, RestoredSourceEpoch);
            await RemotePartitionQueryNativeAssertions.StateAsync(fixture, source, destination,
                sourcePosition, destinationPosition);
        });

    [Test]
    public async Task SourceGrantChangedAfterBothActualLeavesRejectsCompletePageThenFreshGrantIsHealthy()
        => await RemoteDocumentNativeAssertions.RunAsync(async fixture =>
        {
            var flow = new RemotePartitionQueryNativeFlow(fixture);
            flow.Seed();
            flow.GrantDestination();
            var source = RemoteDocumentNativeAssertions.Image(fixture.Source);
            var destination = RemoteDocumentNativeAssertions.Image(fixture.Destination);
            var sourcePosition = fixture.Source.Store.Position;
            var destinationPosition = fixture.Destination.Store.Position;
            PartitionQueryPageV1? page = null;
            var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () =>
                page = await flow.ExecuteAsync(CancellationToken.None, afterActualLeaf: count =>
                {
                    if (count == CompleteLeafCount)
                    {
                        flow.SetSourceGrant(RevokedSourceEpoch, granted: false);
                        source = RemoteDocumentNativeAssertions.Image(fixture.Source);
                        sourcePosition = fixture.Source.Store.Position;
                    }
                    return Task.CompletedTask;
                })) ?? throw new InvalidOperationException(MissingFailure);
            await Assert.That(error.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(error.Message).IsEqualTo(DeniedDetail);
            await Assert.That(page).IsNull();
            await RemotePartitionQueryNativeAssertions.StateAsync(fixture, source, destination,
                sourcePosition, destinationPosition);
            flow.SetSourceGrant(RestoredSourceEpoch, granted: true);
            source = RemoteDocumentNativeAssertions.Image(fixture.Source);
            sourcePosition = fixture.Source.Store.Position;
            await RemotePartitionQueryNativeAssertions.PageAsync(await flow.ExecuteAsync(CancellationToken.None),
                fixture, RestoredSourceEpoch);
            await RemotePartitionQueryNativeAssertions.StateAsync(fixture, source, destination,
                sourcePosition, destinationPosition);
        });

    [Test]
    public async Task ResourcePolicyCasAfterBothActualLeavesRejectsOldPageThenCurrentLiteralSchemaIsHealthy()
        => await RemoteDocumentNativeAssertions.RunAsync(async fixture =>
        {
            var flow = new RemotePartitionQueryNativeFlow(fixture);
            flow.Seed();
            flow.GrantDestination();
            var source = RemoteDocumentNativeAssertions.Image(fixture.Source);
            var destination = RemoteDocumentNativeAssertions.Image(fixture.Destination);
            var sourcePosition = fixture.Source.Store.Position;
            var destinationPosition = fixture.Destination.Store.Position;
            PartitionQueryPageV1? page = null;
            var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () =>
                page = await flow.ExecuteAsync(CancellationToken.None, afterActualLeaf: count =>
                {
                    if (count == CompleteLeafCount)
                    {
                        flow.ChangeSourceResourcePolicy(RemoteDocumentNativeFixture.First,
                            UpdatedResourceVersion, UpdatedClassification);
                        source = RemoteDocumentNativeAssertions.Image(fixture.Source);
                        sourcePosition = fixture.Source.Store.Position;
                    }
                    return Task.CompletedTask;
                })) ?? throw new InvalidOperationException(MissingFailure);
            await Assert.That(error.Code).IsEqualTo(ErrorCode.OwnershipLost);
            await Assert.That(error.Message).IsEqualTo(FenceChangedDetail);
            await Assert.That(page).IsNull();
            await RemotePartitionQueryNativeAssertions.StateAsync(fixture, source, destination,
                sourcePosition, destinationPosition);
            await RemotePartitionQueryNativeAssertions.PageAsync(await flow.ExecuteAsync(CancellationToken.None),
                fixture, sourceSchema: UpdatedResourceVersion);
            await RemotePartitionQueryNativeAssertions.StateAsync(fixture, source, destination,
                sourcePosition, destinationPosition);
        });

    [Test]
    public async Task OriginalCallerCancellationAfterActualNativeLeafReturnsNoPageAndSettlesBeforeHealthyMerge()
        => await RemoteDocumentNativeAssertions.RunAsync(async fixture =>
        {
            var flow = new RemotePartitionQueryNativeFlow(fixture);
            flow.Seed();
            flow.GrantDestination();
            var source = RemoteDocumentNativeAssertions.Image(fixture.Source);
            var destination = RemoteDocumentNativeAssertions.Image(fixture.Destination);
            var sourcePosition = fixture.Source.Store.Position;
            var destinationPosition = fixture.Destination.Store.Position;
            using var caller = new CancellationTokenSource();
            PartitionQueryPageV1? page = null;
            var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
                page = await flow.ExecuteAsync(caller.Token, caller.CancelAsync))
                ?? throw new InvalidOperationException(MissingFailure);
            await Assert.That(error.CancellationToken).IsEqualTo(caller.Token);
            await Assert.That(page).IsNull();
            await RemotePartitionQueryNativeAssertions.StateAsync(fixture, source, destination,
                sourcePosition, destinationPosition);
            await RemotePartitionQueryNativeAssertions.PageAsync(await flow.ExecuteAsync(CancellationToken.None), fixture);
            await RemotePartitionQueryNativeAssertions.StateAsync(fixture, source, destination,
                sourcePosition, destinationPosition);
        });
}
