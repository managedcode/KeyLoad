using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextIncrementalLifecycleTests
{
    private const int TrackedRecords = 2;
    private const string ChangedJson = "{\"text\":\"оновлено changed\"}";
    private const long OriginalRevision = 1;

    [Test]
    public async Task ActualJournalSeedThenNativeUpdateDeleteAndColdRestoreRetainExactCheckpoint()
    {
        TestDatabase? fixture = null;
        NativeTextMaintenanceTestRuntime? runtime = null;
        var failures = new List<Exception>();
        var token = TestContext.Current!.Execution.CancellationToken;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                fixture = new(nativeReplicaAdmission: true);
                fixture.Configure(NativeTextBilingualAudit.Collection, ResourceKind.Collection);
                _ = await NativeTextMaintenanceSeed.CommitAsync(fixture, token);
                var request = await NativeTextMaintenanceRequestFixture.CreateAsync(fixture, token);
                await NativeTextParentBoundaryAssertions.RejectAsync(fixture, request);
                runtime = new(fixture);
                var built = await NativeTextMaintenancePhaseFlow.FinishAsync(fixture, runtime, request, token);
                await Assert.That(built.TrackedRecords).IsEqualTo(TrackedRecords);
                await runtime.DisposeAsync();
                runtime = null;
                var writeId = Guid.NewGuid();
                var write = new CommandRequest(writeId, fixture.Partition,
                    [new PutDocument(NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.UkrainianId,
                        ChangedJson, ExpectedRevision: OriginalRevision),
                        new DeleteDocument(NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.EnglishId,
                            ExpectedRevision: OriginalRevision)]);
                _ = await NativeTextMaintenanceCommit.ExecuteAsync<CommitReceipt>(fixture,
                    OperationKind.Batch, write, writeId, token);
                runtime = new(fixture);
                var restore = request with { CommandId = Guid.NewGuid(), Mode = TextIndexMaintenanceMode.Restore };
                var replayed = await NativeTextMaintenancePhaseFlow.FinishAsync(fixture, runtime, restore, token);
                await Assert.That(replayed.TrackedRecords).IsEqualTo(TrackedRecords);
                await Assert.That(replayed.ThroughSequence).IsGreaterThan(built.ThroughSequence);
                await Assert.That(replayed.IndexSha256).IsNotEqualTo(built.IndexSha256);
                await runtime.DisposeAsync();
                runtime = null;
                await NativeTextIncrementalLiteralOracle.VerifyAsync(fixture, restore, ChangedJson, token);
            }, failures);
        }
        finally
        {
            if (runtime is not null)
            { await ServerFailureObserver.ObserveAsync(() => runtime.DisposeAsync().AsTask(), failures); }
            if (fixture is not null)
            { ServerFailureObserver.Observe(fixture.Dispose, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
