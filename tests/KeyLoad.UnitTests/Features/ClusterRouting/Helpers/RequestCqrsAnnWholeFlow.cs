using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsAnnWholeFlow
{
    internal static async Task RunAsync(RequestCqrsClusterFixture fixture, bool malformed)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var request = await RequestCqrsAnnTestData.SeedAsync(fixture, token);
        var before = fixture.Database.Store.Position;
        var beforeJournal = fixture.Database.ReadJournalCut().LastIndex;
        var built = await RequestCqrsAnnInvocation.RunAsync(fixture, request, token);
        await RequestCqrsAnnResultAssertions.RequireAsync(fixture, request, built.Result, before, changed: false, token);
        await RequestCqrsAnnCheckpointAssertions.RequireAsync(fixture, request, built.Result, beforeJournal);
        await RequestCqrsAnnCheckpointReplay.RunAsync(fixture, request, built.Result, token);
        if (malformed)
        {
            await RequestCqrsAnnProgressControls.RunAsync(fixture, request, built.Reply, token);
            foreach (var fault in Enum.GetValues<RequestCqrsAnnFault>())
            {
                var image = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
                var cut = fixture.Database.Store.Position;
                await RequestCqrsAnnMalformedFlow.RefuseAsync(fixture, request, built.Reply, fault, token);
                await Assert.That(fixture.Database.Store.Position).IsEqualTo(cut);
                await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Database.Store).AsSpan().SequenceEqual(image)).IsTrue();
            }
        }
        await RequestCqrsAnnTestData.ChangeAsync(fixture, token);
        var restore = request with { CommandId = Guid.NewGuid(), Mode = AnnMaintenanceMode.Restore };
        before = fixture.Database.Store.Position;
        beforeJournal = fixture.Database.ReadJournalCut().LastIndex;
        var restored = await RequestCqrsAnnInvocation.RunAsync(fixture, restore, token);
        await RequestCqrsAnnResultAssertions.RequireAsync(fixture, restore, restored.Result, before, changed: true, token);
        await Assert.That(restored.Result.Source!.ThroughSequence).IsGreaterThan(built.Result.Source!.ThroughSequence);
        await Assert.That(restored.Result.IndexSha256).IsNotEqualTo(built.Result.IndexSha256);
        await RequestCqrsAnnCheckpointAssertions.RequireAsync(fixture, restore, restored.Result, beforeJournal);
        await RequestCqrsAnnCheckpointReplay.RunAsync(fixture, restore, restored.Result, token);
        var release = restore with { CommandId = Guid.NewGuid(), Mode = AnnMaintenanceMode.Release };
        var released = await RequestCqrsAnnInvocation.RunAsync(fixture, release, token);
        await RequestCqrsAnnAssertions.EqualAsync(new AnnMaintenanceResult(release.CommandId, release.Consumer,
            release.IndexGeneration, AnnMaintenancePhase.Completed, null, RequestCqrsAnnTestData.EmptyCount, null, null), released.Result);
    }
}
