using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeAnnWaitGenerationTests
{
    private const long Revision = 1;
    private const int FirstRecord = 0;
    private const int SecondRecord = 1;
    private const int FirstDenominator = 61;
    private const int SecondDenominator = 62;
    private const string Json = "{}";
    private const string DeletedId = "seed-00002";

    [Test]
    public Task ActualUpdateDeleteRejectsStaleWaitThenExplicitRestoreAndColdOwnerReturnIndexedPrefixWithoutDeletedRow()
        => NativeAnnWaitWholeFlow.RunAsync(async (fixture, runtime, pin, receipt) =>
        {
            var database = fixture.Canonical;
            var token = TestContext.Current!.Execution.CancellationToken;
            var changed = await NativeAnnWaitWholeFlow.CommitAsync(fixture,
                [new PutVector(AnnSeedTestSupport.Collection, AnnSeedTestSupport.Id(FirstRecord), AnnSeedTestSupport.Field,
                    [3, 0, 0], AnnSeedTestSupport.Space(), Revision),
                 new DeleteDocument(AnnSeedTestSupport.Collection, DeletedId, Revision)], token);
            var request = NativeAnnWaitWholeFlow.Request(database, pin, changed);
            var image = NativeAnnMaintenanceTestData.Snapshot(database);
            var cut = database.Store.Position;
            WaitForAnnIndexResult? partial = null;
            var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial =
                await AnnPublicWholeFlow.Engine(database, runtime).WaitForAnnIndexAsync(NativeAnnWaitWholeFlow.Root, request, token))
                ?? throw new InvalidOperationException();
            await Assert.That(error.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
            await Assert.That(partial).IsNull();
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            await runtime.Owner.AbortAsync(runtime.SessionId);
            await RestoreAsync(database, runtime, pin);
            await NativeAnnWaitWholeFlow.HealthyAsync(database, runtime, request, token);
            await DeletedAsync(database, runtime, pin, token);
            await runtime.Owner.DisposeAsync();
            await using var cold = new NativeAnnMaintenanceTestRuntime(database);
            await RestoreAsync(database, cold, pin);
            await NativeAnnWaitWholeFlow.HealthyAsync(database, cold, request, token);
            await DeletedAsync(database, cold, pin, token);
        });

    private static async Task RestoreAsync(TestDatabase database, NativeAnnMaintenanceTestRuntime runtime, AnnMaintenanceRequest pin)
    {
        var restore = pin with { CommandId = Guid.NewGuid(), Mode = AnnMaintenanceMode.Restore };
        var began = await runtime.PhaseAsync(database, restore, AnnMaintenanceCapabilityKind.Begin);
        _ = await runtime.PhaseAsync(database, restore, AnnMaintenanceCapabilityKind.Load);
        _ = await NativeAnnMaintenancePhaseAssertions.FinishAsync(database, runtime, restore, began.Source!.ThroughSequence);
    }

    private static async Task DeletedAsync(TestDatabase database, NativeAnnMaintenanceTestRuntime runtime,
        AnnMaintenanceRequest pin, CancellationToken token)
    {
        var page = await AnnPublicWholeFlow.Engine(database, runtime).ApproximateSearchAsync(NativeAnnWaitWholeFlow.Root,
            AnnPublicWholeFlow.Request(database, pin), token);
        var expected = new AnnSearchPage(AnnPublicWholeFlow.Version,
            [Row(database, FirstRecord, FirstDenominator), Row(database, SecondRecord, SecondDenominator)],
            database.Store.Position, AnnPageMode.Exact, true, pin.IndexGeneration, AnnPageMode.Approximate);
        await Assert.That(System.Text.Json.JsonSerializer.Serialize(page, JsonDefaults.Options))
            .IsEqualTo(System.Text.Json.JsonSerializer.Serialize(expected, JsonDefaults.Options));
        await Assert.That(page.Documents.Any(row => row.Document.Reference.Id == DeletedId)).IsFalse();
    }

    private static RankedDocument Row(TestDatabase database, int record, int denominator)
        => new(new(new(database.Partition, AnnSeedTestSupport.Collection, AnnSeedTestSupport.Id(record)),
            Revision, Json, false, []), 1d / denominator);
}
