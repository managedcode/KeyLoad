using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Proves expired signed retirement-envelope denial leaves the intact original grant/effect undispatched.</summary>
internal static class ControlledPartitionMovementRetireExpiryAssertions
{
    internal static async Task AssertAsync(ControlledPartitionMovementNode source, ServerRuntimeOptions runtime,
        PartitionMovementPeerAdmission admission, PartitionMovementTransportRequest original,
        CancellationToken cancellationToken)
    {
        var before = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var position = source.Store.Position;
        var last = source.Journal.Log.State.LastIndex;
        var committed = source.Journal.Log.State.CommittedIndex;
        var applied = source.Database.LastApplied;
        var expired = original with
        { Envelope = original.Envelope with { ExpiresAt = source.Database.EvaluationClock.GetUtcNow() } };
        var denied = await Assert.ThrowsAsync<KeyLoadException>(() =>
            ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission, expired, cancellationToken));
        await Assert.That(denied).IsNotNull();
        var actual = denied ?? throw new InvalidOperationException("The actual expired retirement envelope was not denied.");
        await Assert.That(actual.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(last);
        await Assert.That(source.Journal.Log.State.CommittedIndex).IsEqualTo(committed);
        await Assert.That(source.Database.LastApplied).IsEqualTo(applied);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store)
            .SequenceEqual(before, StringComparer.Ordinal)).IsTrue();
    }
}
