using KeyLoad.Core.Features.Search;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Messaging;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.Search;

internal static class ProjectionDigestWholeFlow
{
    internal static async Task RunAsync(bool effect)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new EventProjectionFixture();
            await ServerFailureObserver.ObserveAsync(() => ExecuteAsync(fixture, effect), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(EventProjectionFixture fixture, bool effect)
    {
        var malformed = await ProjectionDigestAssertions.MalformedOwnerAsync(fixture);
        await ReplayAsync(fixture, malformed.Command, malformed.Result);
        var request = fixture.Request();
        var command = new CommandRequest(Guid.NewGuid(), fixture.Partition, [request]);
        var beforeCommit = fixture.Harness.Store.Position;
        var result = fixture.Harness.Submit(OperationKind.Batch, command, EventProjectionFixture.WorkerId, command.CommandId);
        var receipt = result.Get<CommitReceipt>();
        await ProjectionDigestAssertions.ReceiptAsync(fixture, command, receipt, beforeCommit);
        var vector = fixture.VectorBytes()!;
        var lineage = fixture.Harness.Store.Read(view => view.ReadOwnedValue(fixture.LineageKey()))!;
        var key = effect ? VectorProjectionKeys.Effect(fixture.Partition, request) : fixture.VectorKey();
        var original = fixture.Harness.Store.Read(view => view.ReadOwnedValue(key))!;
        var damaged = ProjectionDigestImage.Damaged(original, effect);
        var continuation = new CommandRequest(Guid.NewGuid(), fixture.Partition, [request]);
        var image = QueueWholeFlowStorage.Bytes(fixture.Harness.Store);
        var position = fixture.Harness.Store.Position;
        await FaultAndRepairAsync(fixture, effect, key, original, damaged, image, position, continuation);
        Result<object>? continued = null;
        if (!effect)
        {
            var beforeRepair = fixture.Harness.Store.Position;
            var actual = fixture.Harness.Submit(OperationKind.Batch, continuation, EventProjectionFixture.WorkerId, continuation.CommandId);
            continued = actual;
            await ProjectionDigestAssertions.ReceiptAsync(fixture, continuation, actual.Get<CommitReceipt>(), beforeRepair);
            await ReplayAsync(fixture, continuation, actual);
        }
        await ProjectionDigestAssertions.HealthyAsync(fixture, schema: 1L);
        await ReplayAsync(fixture, command, result);
        var repairedImage = QueueWholeFlowStorage.Bytes(fixture.Harness.Store);
        var repairedCut = fixture.Harness.Store.Position;
        await ProjectionDigestImage.ColdAsync(fixture, repairedImage, repairedCut);
        await ClassificationLineageWholeAssertions.RetainedAsync(fixture, vector, lineage);
        await ReplayAsync(fixture, malformed.Command, malformed.Result);
        await ProjectionDigestAssertions.HealthyAsync(fixture, schema: 1L);
        await ReplayAsync(fixture, command, result);
        if (continued is { } observed)
        {
            await ReplayAsync(fixture, continuation, observed);
        }
        await ProjectionDigestReclassification.RunAsync(fixture, request);
        await ReplayAsync(fixture, malformed.Command, malformed.Result);
    }

    private static async Task FaultAndRepairAsync(EventProjectionFixture fixture, bool effect, byte[] key,
        byte[] original, byte[] damaged, string[] image, long position, CommandRequest continuation)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            ProjectionDigestImage.Write(fixture, key, damaged);
            var fault = ProjectionDigestImage.Replace(image, key, original, damaged);
            await ClassificationLineageWholeAssertions.UnchangedAsync(fixture, fault, position + 1L);
            await ProjectionDigestImage.ObserveAsync(fixture, () => ProjectionDigestAssertions.RefusedAsync(fixture));
            if (!effect)
            {
                await ProjectionDigestImage.ObserveAsync(fixture,
                    () => ProjectionDigestAssertions.RefusedApplyAsync(fixture, continuation));
            }
        }, failures);
        ServerFailureObserver.Observe(() => ProjectionDigestImage.Write(fixture, key, original), failures);
        await ServerFailureObserver.ObserveAsync(() => ClassificationLineageWholeAssertions.UnchangedAsync(
            fixture, image, position + 2L), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ReplayAsync(EventProjectionFixture fixture, CommandRequest command, Result<object> result)
        => await ProjectionDigestImage.ObserveAsync(fixture, async () =>
        {
            var replay = fixture.Harness.Submit(OperationKind.Batch, command, EventProjectionFixture.WorkerId, command.CommandId);
            await Assert.That(OrleansSerialization.Serialize(replay).AsSpan().SequenceEqual(OrleansSerialization.Serialize(result))).IsTrue();
        });
}
