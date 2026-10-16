using KeyLoad.Clustering;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class ControlledBlobLifetimeReadHold
{
    private readonly RequestCqrsProbeFixture fixture;
    private readonly PartitionRef partition;
    private readonly GrainReadKind kind;
    private readonly Guid armId;
    private RequestCqrsProbeMarkerRecord? held;
    private bool released;

    internal ControlledBlobLifetimeReadHold(RequestCqrsProbeFixture fixture, PartitionRef partition, GrainReadKind kind)
    {
        this.fixture = fixture;
        this.partition = partition;
        this.kind = kind;
        armId = fixture.WriteArm(PartitionMovementPublicParentRf3Administrator.PrincipalId, Guid.Empty, kind,
            RequestCqrsProbePhase.NativeModelAfterDispatch, RequestCqrsProbeAction.Hold);
    }

    internal async Task<DateTimeOffset> WaitAsync(Task original, IReadOnlyList<ReplicaSiloDiscovery> discovery,
        CancellationToken token)
    {
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token);
        var observed = fixture.WaitForMarkerAsync(armId, RequestCqrsProbePhase.NativeModelAfterDispatch,
            RequestCqrsProbeOutcome.Observed, discovery, waiting.Token);
        if (await Task.WhenAny(observed, original).ConfigureAwait(false) == original)
        {
            await waiting.CancelAsync().ConfigureAwait(false);
            try
            {
                _ = await observed.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (waiting.IsCancellationRequested) { }
            await original.ConfigureAwait(false);
            throw new InvalidOperationException(RequestCqrsProbeFixtureProtocol.MarkerMismatch);
        }
        var marker = await observed.ConfigureAwait(false);
        held = marker;
        var node = RequestCqrsProbeFixtureProtocol.Nodes.Single(value =>
            RequestCqrsProbeFileNames.OriginForNode(value) == marker.Voter);
        var owner = fixture.NodeFor(node);
        RequestCqrsProbeFileStore.VerifyOwnerFile(owner.Directory, owner.OwnerBytes);
        var witness = RequestCqrsNativeModelInventory.Read(RequestCqrsProbeFileStore.ReadRecord(Path.Combine(
            owner.Directory, RequestCqrsNativeModelInventory.Name(armId, marker.RequestId, marker.Phase))));
        await RequireWitnessAsync(witness, marker).ConfigureAwait(false);
        var native = RequestCqrsNativeModelInventory.Request(witness);
        var call = NativeSerialization.Deserialize<RemoteResourceLifetimeReadCallV2>(native.Payload.Span);
        var request = NativeSerialization.Deserialize<ControlledBlobReadRequestV2>(call.Payload.Span);
        await Assert.That(native.Channel).IsEqualTo(RemoteModelNativeChannel.ResourceLifetimeRead);
        await Assert.That(RequestCqrsNativeModelInventory.Reply(witness).Error).IsNull();
        await Assert.That(call.Kind).IsEqualTo(RemoteResourceLifetimeReadKindV2.ControlledBlob);
        await Assert.That(request.Frame.Original.Control.Partition).IsEqualTo(partition);
        await Assert.That(request.Frame.Coverage.Partition).IsEqualTo(partition);
        await Assert.That(request.Frame.Coverage.Lifetimes).HasSingleItem();
        await Assert.That(request.Frame.Original.Purpose).IsNotEqualTo(ControlledBlobReadPurpose.Outcome);
        await Assert.That(call.ExpiresAt).IsEqualTo(request.Frame.Original.ExpiresAt);
        await RequireScopePredicatesAsync(native, call, request, marker.RequestId).ConfigureAwait(false);
        return call.ExpiresAt;
    }

    private async Task RequireWitnessAsync(RequestCqrsNativeModelWitness witness, RequestCqrsProbeMarkerRecord marker)
    {
        await Assert.That(witness.SessionId).IsEqualTo(fixture.SessionId);
        await Assert.That(witness.ArmId).IsEqualTo(armId);
        await Assert.That(witness.RequestId).IsEqualTo(marker.RequestId);
        await Assert.That(witness.CommandId).IsEqualTo(Guid.Empty);
        await Assert.That(marker.CommandId).IsEqualTo(Guid.Empty);
        await Assert.That(witness.ConnectionId).IsNotEqualTo(Guid.Empty);
        await Assert.That(witness.Phase).IsEqualTo(marker.Phase);
        await Assert.That(witness.Voter).IsEqualTo(marker.Voter);
        await Assert.That(witness.SiloAddress).IsEqualTo(marker.SiloAddress);
    }

    private async Task RequireScopePredicatesAsync(RemoteModelNativeRequest native,
        RemoteResourceLifetimeReadCallV2 call, ControlledBlobReadRequestV2 request, Guid parentRequestId)
    {
        await Assert.That(RequestCqrsLifetimeReadProbe.MatchesCall(native, kind, parentRequestId)).IsTrue();
        RequestCqrsLifetimeReadProbe.RequireReadWitness(native, parentRequestId);
        foreach (var other in ControlledBlobLifetimeReadCalls.Kinds.Where(value => value != kind))
        {
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                RequestCqrsLifetimeReadProbe.MatchesCall(native, other, parentRequestId));
        }
        // These are diagnostic scope-predicate variants of the actual frame, never authenticated endpoint calls.
        var outcome = request with
        {
            Frame = request.Frame with
            {
                Original = request.Frame.Original with
                {
                    Purpose = ControlledBlobReadPurpose.Outcome
                }
            }
        };
        var changedCall = call with
        {
            Payload = NativeSerialization.Serialize(outcome)
        };
        var changedNative = native with
        {
            Payload = NativeSerialization.Serialize(changedCall)
        };
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            RequestCqrsLifetimeReadProbe.MatchesCall(changedNative, kind, parentRequestId));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            RequestCqrsLifetimeReadProbe.RequireReadWitness(changedNative, parentRequestId));
    }

    internal void Release()
    {
        if (!released && held is { } marker)
        {
            fixture.WriteRelease(armId, marker.RequestId);
            released = true;
        }
    }

    internal async Task JoinAsync(IReadOnlyList<ReplicaSiloDiscovery> discovery, CancellationToken token)
    {
        if (held is not null)
        {
            await fixture.WaitForSettlementAsync(armId, discovery, token).ConfigureAwait(false);
        }
        await fixture.RetireArmAsync(armId, token).ConfigureAwait(false);
    }
}
