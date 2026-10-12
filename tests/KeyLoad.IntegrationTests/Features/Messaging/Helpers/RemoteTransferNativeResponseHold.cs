using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class RemoteTransferNativeResponseHold
{
    private readonly RequestCqrsProbeFixture fixture;
    private readonly Guid commandId;
    private readonly Guid armId;
    private RequestCqrsProbeMarkerRecord? held;
    private bool released;

    internal RemoteTransferNativeResponseHold(RequestCqrsProbeFixture fixture, string principalId,
        Guid commandId, PartitionRef partition)
    {
        this.fixture = fixture;
        this.commandId = commandId;
        armId = fixture.WriteArm(principalId, commandId, null,
            RequestCqrsProbePhase.NativeModelAfterDispatch, RequestCqrsProbeAction.Hold, partition);
    }

    internal async Task<RequestCqrsNativeModelWitness> WaitAsync(Task original,
        IReadOnlyList<ReplicaSiloDiscovery> sourceDiscovery, CancellationToken token)
    {
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token);
        var observed = fixture.WaitForMarkerAsync(armId, RequestCqrsProbePhase.NativeModelAfterDispatch,
            RequestCqrsProbeOutcome.Observed, sourceDiscovery, waiting.Token);
        if (await Task.WhenAny(observed, original).ConfigureAwait(false) == original)
        {
            await waiting.CancelAsync().ConfigureAwait(false);
            try
            { _ = await observed.ConfigureAwait(false); }
            catch (OperationCanceledException) when (waiting.IsCancellationRequested) { }
            await original.ConfigureAwait(false);
            throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
        }
        var marker = await observed.ConfigureAwait(false);
        held = marker;
        var node = RequestCqrsProbeFixtureProtocol.Nodes.Single(value =>
            RequestCqrsProbeFileNames.OriginForNode(value) == marker.Voter);
        var owner = fixture.NodeFor(node);
        RequestCqrsProbeFileStore.VerifyOwnerFile(owner.Directory, owner.OwnerBytes);
        var name = RequestCqrsNativeModelInventory.Name(armId, marker.RequestId, marker.Phase);
        var witness = RequestCqrsNativeModelInventory.Read(
            RequestCqrsProbeFileStore.ReadRecord(Path.Combine(owner.Directory, name)));
        if (witness.SessionId != fixture.SessionId || witness.ArmId != armId
            || witness.RequestId != marker.RequestId || witness.CommandId != commandId
            || marker.CommandId != commandId || witness.ConnectionId == Guid.Empty
            || witness.Phase != marker.Phase || witness.Voter != marker.Voter
            || witness.SiloAddress != marker.SiloAddress)
        { throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing); }
        return witness;
    }

    internal void Release()
    {
        if (!released && held is { } marker)
        {
            fixture.WriteRelease(armId, marker.RequestId);
            released = true;
        }
    }

    internal async Task JoinAsync(IReadOnlyList<ReplicaSiloDiscovery> sourceDiscovery, CancellationToken token)
    {
        if (held is not null)
        { await fixture.WaitForSettlementAsync(armId, sourceDiscovery, token).ConfigureAwait(false); }
        await fixture.RetireArmAsync(armId, token).ConfigureAwait(false);
    }
}
