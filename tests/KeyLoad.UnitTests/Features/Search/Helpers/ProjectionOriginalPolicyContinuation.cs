using KeyLoad.Server.Features.Search;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Search;

internal static class ProjectionOriginalPolicyContinuation
{
    private const string NativeDirectory = "projection-original-policy-text";
    private const string MissingLineage = "The original projection lineage is absent.";
    private const string AcceptedOriginal = "The original projection outcome crossed its current policy fence.";

    internal static async Task RunAsync(EventProjectionFixture fixture, ApplyVectorProjection request,
        Guid originalCommandId, MutationReceipt original, byte[] vector)
    {
        var lineage = fixture.Harness.Store.Read(view => view.ReadOwnedValue(fixture.LineageKey()))
            ?? throw new InvalidOperationException(MissingLineage);
        await ClassificationLineageWholeAssertions.ReceiptAsync(fixture, original);
        await ClassificationLineageWholeAssertions.RetainedAsync(fixture, vector, lineage);
        fixture.ConfigureWorker(revoked: true);
        await RefusedAsync(fixture, request, originalCommandId, ErrorCode.Unauthenticated);
        fixture.ConfigureWorker();
        await RefusedAsync(fixture, request, originalCommandId, ErrorCode.PermissionDenied);
        var healthyCommandId = Guid.NewGuid();
        var healthy = fixture.Apply(request, healthyCommandId);
        await ClassificationLineageWholeAssertions.ReplayReceiptAsync(healthy, original);
        await ClassificationLineageWholeAssertions.ReceiptAsync(fixture, healthy);
        await ClassificationLineageWholeAssertions.RetainedAsync(fixture, vector, lineage);
        await HealthyReadAsync(fixture);
        await ReplayAsync(fixture, request, healthyCommandId, healthy);
        var image = QueueWholeFlowStorage.Bytes(fixture.Harness.Store);
        var cut = fixture.Harness.Store.Position;
        var identity = fixture.Harness.Store.Identity;
        fixture.Harness.Reopen();
        await Assert.That(fixture.Harness.Store.Identity.NodeId).IsEqualTo(identity.NodeId);
        await Assert.That(fixture.Harness.Store.Identity.Incarnation).IsEqualTo(identity.Incarnation);
        await Assert.That(fixture.Harness.Store.Identity.ReadGeneration).IsGreaterThanOrEqualTo(identity.ReadGeneration);
        await ClassificationLineageWholeAssertions.UnchangedAsync(fixture, image, cut);
        await RefusedAsync(fixture, request, originalCommandId, ErrorCode.PermissionDenied);
        await ReplayAsync(fixture, request, healthyCommandId, healthy);
        await ClassificationLineageWholeAssertions.RetainedAsync(fixture, vector, lineage);
        await HealthyReadAsync(fixture);
        await ClassificationLineageWholeAssertions.UnchangedAsync(fixture, image, cut);
    }

    private static async Task RefusedAsync(EventProjectionFixture fixture, ApplyVectorProjection request,
        Guid commandId, ErrorCode code)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Harness.Store);
        var cut = fixture.Harness.Store.Position;
        var refusal = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Apply(request, commandId))
            ?? throw new InvalidOperationException(AcceptedOriginal);
        await Assert.That(refusal.Code).IsEqualTo(code);
        await ClassificationLineageWholeAssertions.UnchangedAsync(fixture, image, cut);
    }

    private static async Task ReplayAsync(EventProjectionFixture fixture, ApplyVectorProjection request,
        Guid commandId, MutationReceipt original)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Harness.Store);
        var cut = fixture.Harness.Store.Position;
        await ClassificationLineageWholeAssertions.ReplayReceiptAsync(fixture.Apply(request, commandId), original);
        await ClassificationLineageWholeAssertions.UnchangedAsync(fixture, image, cut);
    }

    private static async Task HealthyReadAsync(EventProjectionFixture fixture)
    {
        var projection = new NativeTextProjection(Path.Combine(fixture.Harness.DirectoryPath, NativeDirectory),
            UnitExecutionOptions.DatabaseLimits(fixture.Database.Limits), fixture.Harness.Store.Identity.NodeId,
            UnitNativeTextOptions.Execution());
        try
        {
            await ClassificationLineageWholeAssertions.HealthyAsync(fixture, projection);
        }
        catch (Exception primary)
        {
            try
            {
                projection.Dispose();
            }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
        projection.Dispose();
    }
}
