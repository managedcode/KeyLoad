using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextIncrementalLifecycleTests
{
    private const int TrackedRecords = 2;
    private const string ChangedJson = "{\"text\":\"оновлено changed\"}";
    private const long OriginalRevision = 1;

    [Test]
    public async Task ActualJournalSeedThenUpdateDeleteAndColdRestoreRetainExactCheckpoint()
    {
        TestDatabase? fixture = null;
        TextMaintenanceTestRuntime? runtime = null;
        var failures = new List<Exception>();
        var token = TestContext.Current!.Execution.CancellationToken;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                fixture = new();
                fixture.Configure(TextBilingualAudit.Collection, ResourceKind.Collection);
                _ = await TextMaintenanceSeed.CommitAsync(fixture, token);
                var request = await TextMaintenanceRequestFixture.CreateAsync(fixture, token);
                await TextParentBoundaryAssertions.RejectAsync(fixture, request);
                runtime = new(fixture);
                var built = await TextMaintenancePhaseFlow.FinishAsync(fixture, runtime, request, token);
                await Assert.That(built.TrackedRecords).IsEqualTo(TrackedRecords);
                await runtime.DisposeAsync();
                runtime = null;
                var writeId = Guid.NewGuid();
                var write = new CommandRequest(writeId, fixture.Partition,
                    [new PutDocument(TextBilingualAudit.Collection, TextBilingualAudit.UkrainianId,
                        ChangedJson, ExpectedRevision: OriginalRevision),
                        new DeleteDocument(TextBilingualAudit.Collection, TextBilingualAudit.EnglishId,
                            ExpectedRevision: OriginalRevision)]);
                _ = await TextMaintenanceCommit.ExecuteAsync<CommitReceipt>(fixture,
                    OperationKind.Batch, write, writeId, token);
                runtime = new(fixture);
                var restore = request with
                {
                    CommandId = Guid.NewGuid(),
                    Mode = TextIndexMaintenanceMode.Restore
                };
                var replayed = await TextMaintenancePhaseFlow.FinishAsync(fixture, runtime, restore, token);
                await Assert.That(replayed.TrackedRecords).IsEqualTo(TrackedRecords);
                await Assert.That(replayed.ThroughSequence).IsGreaterThan(built.ThroughSequence);
                await Assert.That(replayed.IndexSha256).IsNotEqualTo(built.IndexSha256);
                await runtime.DisposeAsync();
                runtime = null;
                await TextIncrementalLiteralOracle.VerifyAsync(fixture, restore, ChangedJson, token);
            }, failures);
        }
        finally
        {
            if (runtime is not null)
            {
                await ServerFailureObserver.ObserveAsync(() => runtime.DisposeAsync().AsTask(), failures);
            }
            if (fixture is not null)
            {
                ServerFailureObserver.Observe(fixture.Dispose, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
