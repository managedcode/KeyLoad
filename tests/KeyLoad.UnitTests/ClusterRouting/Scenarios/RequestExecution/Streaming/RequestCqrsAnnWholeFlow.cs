using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsAnnWholeFlow
{
    internal static async Task RunAsync(RequestCqrsHostedRuntimeFixture fixture, bool malformed)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var request = await RequestCqrsAnnTestData.SeedAsync(fixture, token);
        var originalReservationBytes = await RequestCqrsAnnEnrollmentFlow.ReserveAsync(fixture, request, token);
        var before = fixture.Database.Store.Position;
        var beforeJournal = fixture.Database.ReadCanonicalCut().AppliedPosition;
        var built = await RequestCqrsAnnInvocation.RunAsync(fixture, request, token);
        await RequestCqrsAnnEnrollmentFlow.RequireSettledAsync(fixture, request, built.Result, originalReservationBytes);
        await RequestCqrsAnnResultAssertions.RequireAsync(fixture, request, built.Result, before, changed: false, token);
        await RequestCqrsAnnCheckpointAssertions.RequireAsync(fixture, request, built.Result, beforeJournal);
        await RequestCqrsAnnCheckpointReplay.RunAsync(fixture, request, built.Result, token);
        await RequestCqrsAnnGraphIdentityReplay.RequireAsync(fixture, request, built.Result, token);
        await RequestCqrsAnnOriginalReplay.RequireAsync(fixture, request, built.Result, token);
        await RequestCqrsAnnOriginalReplay.RefuseChangedAsync(fixture, request, token);
        await RequestCqrsAnnOriginalReplay.RequireAsync(fixture, request, built.Result, token);
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
        var restore = request with
        {
            CommandId = Guid.NewGuid(),
            Mode = AnnMaintenanceMode.Restore
        };
        before = fixture.Database.Store.Position;
        beforeJournal = fixture.Database.ReadCanonicalCut().AppliedPosition;
        var restored = await RequestCqrsAnnInvocation.RunAsync(fixture, restore, token);
        await RequestCqrsAnnResultAssertions.RequireAsync(fixture, restore, restored.Result, before, changed: true, token);
        await Assert.That(restored.Result.Source!.ThroughSequence).IsGreaterThan(built.Result.Source!.ThroughSequence);
        await Assert.That(restored.Result.IndexSha256).IsNotEqualTo(built.Result.IndexSha256);
        await RequestCqrsAnnCheckpointAssertions.RequireAsync(fixture, restore, restored.Result, beforeJournal);
        await RequestCqrsAnnCheckpointReplay.RunAsync(fixture, restore, restored.Result, token);
        await RequestCqrsAnnOriginalReplay.RefusePriorAsync(fixture, request, token);
        await RequestCqrsAnnOriginalReplay.RequireAsync(fixture, restore, restored.Result, token);
        var noOp = await NoOpSequenceAsync(fixture, restore, restored.Result, token);
        var originalLeaf = RequestCqrsAnnResultAssertions.Manifest(fixture, noOp).GenerationLeaf;
        await RequestCqrsAnnPinnedRetirementFlow.RunAsync(fixture, request, noOp, originalLeaf, token);
    }
    private static async Task<AnnMaintenanceRequest> NoOpSequenceAsync(RequestCqrsHostedRuntimeFixture fixture,
        AnnMaintenanceRequest restore, AnnMaintenanceResult restored, CancellationToken token)
    {
        var noOp = restore with
        {
            CommandId = Guid.NewGuid()
        };
        var before = fixture.Database.Store.Position;
        var applied = fixture.Database.ReadCanonicalCut().AppliedPosition;
        var unchanged = await RequestCqrsAnnInvocation.RunAsync(fixture, noOp, token);
        await NoOpResultAsync(fixture, noOp, unchanged.Result, restored, before, applied, token);
        await Assert.That(unchanged.Result.Count).IsEqualTo(restored.Count);
        await Assert.That(unchanged.Result.IndexSha256).IsEqualTo(restored.IndexSha256);
        await RequestCqrsAnnAssertions.EqualAsync(restored.Checkpoint, unchanged.Result.Checkpoint);
        await RequestCqrsAnnOriginalReplay.RequireAsync(fixture, noOp, unchanged.Result, token);
        await RequestCqrsAnnOriginalReplay.RefusePriorAsync(fixture, restore, token);
        await RequestCqrsAnnOriginalReplay.RefuseChangedAsync(fixture, restore, token);
        await RequestCqrsAnnOriginalReplay.RequireAsync(fixture, noOp, unchanged.Result, token);
        var fresh = noOp with
        {
            CommandId = Guid.NewGuid()
        };
        before = fixture.Database.Store.Position;
        applied = fixture.Database.ReadCanonicalCut().AppliedPosition;
        var healthy = await RequestCqrsAnnInvocation.RunAsync(fixture, fresh, token);
        await NoOpResultAsync(fixture, fresh, healthy.Result, restored, before, applied, token);
        await Assert.That(healthy.Result.Count).IsEqualTo(RequestCqrsAnnTestData.ChangedCount);
        await Assert.That(healthy.Result.IndexSha256).IsEqualTo(restored.IndexSha256);
        await RequestCqrsAnnAssertions.EqualAsync(restored.Checkpoint, healthy.Result.Checkpoint);
        await RequestCqrsAnnOriginalReplay.RefusePriorAsync(fixture, noOp, token);
        await RequestCqrsAnnOriginalReplay.RequireAsync(fixture, fresh, healthy.Result, token);
        return fresh;
    }

    private static async Task NoOpResultAsync(RequestCqrsHostedRuntimeFixture fixture, AnnMaintenanceRequest request,
        AnnMaintenanceResult actual, AnnMaintenanceResult prior, long before, long applied, CancellationToken token)
    {
        var source = actual.Source ?? throw new InvalidOperationException();
        var original = prior.Source ?? throw new InvalidOperationException();
        await Assert.That(source.NodeId).IsEqualTo(fixture.Database.Store.Identity.NodeId);
        await Assert.That(source.Incarnation).IsEqualTo(fixture.Database.Store.Identity.Incarnation);
        await Assert.That(source.Position).IsGreaterThanOrEqualTo(before);
        await Assert.That(source.Position).IsLessThanOrEqualTo(fixture.Database.Store.Position);
        await Assert.That(source.AppliedPosition).IsGreaterThanOrEqualTo(applied);
        await Assert.That(source.AppliedPosition).IsLessThanOrEqualTo(fixture.Database.ReadCanonicalCut().AppliedPosition);
        await Assert.That(source.ReadGeneration).IsEqualTo(fixture.Database.Store.Identity.ReadGeneration);
        await Assert.That(source.StoreFormatVersion).IsEqualTo(fixture.Database.Store.Identity.FormatVersion);
        await Assert.That(source.KeyCodecVersion).IsEqualTo(fixture.Database.Store.Identity.KeyCodecVersion);
        await Assert.That(source.ThroughSequence).IsEqualTo(RequestCqrsAnnTestData.ChangedTail);
        await Assert.That(source.SchemaVersion).IsEqualTo(original.SchemaVersion);
        await Assert.That(source.PolicyEpoch).IsEqualTo(original.PolicyEpoch);
        await Assert.That(source.CorpusSha256).IsEqualTo(original.CorpusSha256);
        await Assert.That(source.DependencySha256).IsEqualTo(original.DependencySha256);
        await Assert.That(source.OutboxFirstAvailable).IsEqualTo(fixture.Database.Database
            .GetOutboxStatus(RequestCqrsAnnTestData.Principal, request.Consumer.Partition).Head.FirstAvailable);
        await RequestCqrsAnnAssertions.EqualAsync(new AnnMaintenanceResult(request.CommandId, request.Consumer,
            request.IndexGeneration, AnnMaintenancePhase.Completed, source, RequestCqrsAnnTestData.ChangedCount,
            prior.IndexSha256, prior.Checkpoint), actual);
        await RequestCqrsAnnResultAssertions.SearchAsync(fixture, request, changed: true, token);
    }

}
