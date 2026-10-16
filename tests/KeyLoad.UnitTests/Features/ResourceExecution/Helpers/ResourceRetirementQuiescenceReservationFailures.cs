using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementQuiescenceData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementQuiescenceReservationFailures
{
    internal static async Task<OperationResult> SubmitAsync(ResourceRetirementQuiescenceState state,
        ControlledPartitionMovementNode owner, ReplicatedOperation original, ErrorCode error, string? detail, CancellationToken token)
    {
        var source = ControlledPartitionMovementExpiryOwnerState.Capture(state.Source);
        var target = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        var before = ReferenceEquals(owner, state.Source) ? source : target;
        var result = owner.Journal.Submit(original, token);
        await Assert.That(result.Error).IsEqualTo(error);
        if (detail is not null)
        {
            await Assert.That(result.SafeDetail).IsEqualTo(detail);
        }
        await Assert.That(result.NativeValue).IsNull();
        await Assert.That(result.Json).IsNull();
        var key = KeySpace.GlobalOutcome(original.PrincipalId, original.Id);
        var stored = owner.Store.Read(view => view.GetRecord<StoredOutcome>(key)) ?? throw new InvalidOperationException(Invalid);
        await ResourceRetirementQuiescenceAssertions.EqualAsync(result, stored.Result);
        await Assert.That(stored.Fingerprint).IsEqualTo(NativeOperationFingerprint.Compute(original));
        await Assert.That(stored.Incarnation).IsEqualTo(owner.Store.Identity.Incarnation);
        await Assert.That(stored.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Global);
        var allowed = new[] { KeySpace.AppliedBytes, KeySpace.ClockBytes, key }
            .Select(value => Convert.ToHexString(value) + ":").ToArray();
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(owner.Store)
            .Where(row => !allowed.Any(prefix => row.StartsWith(prefix, StringComparison.Ordinal)))
            .SequenceEqual(before.Image.Where(row => !allowed.Any(prefix => row.StartsWith(prefix, StringComparison.Ordinal))),
                StringComparer.Ordinal)).IsTrue();
        await Assert.That(owner.Store.Position).IsEqualTo(before.Position + One);
        await Assert.That(owner.Journal.Log.State.LastIndex).IsEqualTo(before.LastIndex + One);
        await Assert.That(owner.Journal.Log.State.CommittedIndex).IsEqualTo(before.CommittedIndex + One);
        await Assert.That(owner.Database.LastApplied).IsEqualTo(before.LastIndex + One);
        if (ReferenceEquals(owner, state.Source))
        {
            await target.AssertUnchangedAsync(state.Target);
        }
        else
        {
            await source.AssertUnchangedAsync(state.Source);
        }
        state.Originals.Add((owner, original, result));
        return result;
    }
}
