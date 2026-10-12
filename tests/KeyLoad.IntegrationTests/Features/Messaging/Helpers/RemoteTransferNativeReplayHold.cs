using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class RemoteTransferNativeReplayHold
{
    private readonly RequestCqrsProbeFixture fixture;
    private readonly Guid commandId;
    private RequestCqrsProbeMarkerRecord? held;
    private bool released;

    internal RemoteTransferNativeReplayHold(RequestCqrsProbeFixture fixture, string principalId,
        CommandRequest command, RemoteModelNativeReplayVariation mode)
    {
        this.fixture = fixture;
        commandId = command.CommandId;
        ArmId = fixture.WriteArm(principalId, commandId, null,
            RequestCqrsProbePhase.NativeModelReplayBeforeDispatch, RequestCqrsProbeAction.Hold, command.Partition);
        fixture.WriteReplayPlan(ArmId, mode);
    }

    internal Guid ArmId { get; }

    internal async Task<IReadOnlyList<RequestCqrsNativeReplayWitness>> WaitAsync(Task original,
        IReadOnlyList<ReplicaSiloDiscovery> source, int count, CancellationToken token)
    {
        if (count is not (2 or 6)) { throw Missing(); }
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(token);
        var observed = fixture.WaitForMarkerAsync(ArmId, RequestCqrsProbePhase.NativeModelReplayBeforeDispatch,
            RequestCqrsProbeOutcome.Observed, source, waiting.Token);
        if (await Task.WhenAny(observed, original).ConfigureAwait(false) == original)
        {
            await waiting.CancelAsync().ConfigureAwait(false);
            try { _ = await observed.ConfigureAwait(false); }
            catch (OperationCanceledException) when (waiting.IsCancellationRequested) { }
            await original.ConfigureAwait(false);
            throw Missing();
        }
        var marker = await observed.ConfigureAwait(false);
        held = marker;
        var node = RequestCqrsProbeFixtureProtocol.Nodes.Single(value =>
            RequestCqrsProbeFileNames.OriginForNode(value) == marker.Voter);
        var owner = fixture.NodeFor(node);
        RequestCqrsProbeFileStore.VerifyOwnerFile(owner.Directory, owner.OwnerBytes);
        var witnesses = new List<RequestCqrsNativeReplayWitness>();
        for (var ordinal = 1; ordinal <= count; ordinal++)
        {
            token.ThrowIfCancellationRequested();
            var name = RequestCqrsNativeReplayInventory.WitnessName(ArmId, marker.RequestId, ordinal);
            var witness = RequestCqrsNativeReplayInventory.ReadWitness(
                RequestCqrsProbeFileStore.ReadRecord(Path.Combine(owner.Directory, name)));
            RequireContext(witness, marker, ordinal);
            witnesses.Add(witness);
        }
        return witnesses;
    }

    private void RequireContext(RequestCqrsNativeReplayWitness witness, RequestCqrsProbeMarkerRecord marker, int ordinal)
    {
        if (witness.SessionId != fixture.SessionId || witness.ArmId != ArmId || witness.Ordinal != ordinal
            || witness.RequestId != marker.RequestId || witness.CommandId != commandId || marker.CommandId != commandId
            || witness.ConnectionId == Guid.Empty || witness.Voter != marker.Voter || witness.SiloAddress != marker.SiloAddress)
        { throw Missing(); }
    }

    internal void Release()
    {
        if (!released && held is { } marker)
        {
            fixture.WriteRelease(ArmId, marker.RequestId);
            released = true;
        }
    }

    private static InvalidOperationException Missing() => new(RemoteTransferDistinctProtocol.Missing);
}
