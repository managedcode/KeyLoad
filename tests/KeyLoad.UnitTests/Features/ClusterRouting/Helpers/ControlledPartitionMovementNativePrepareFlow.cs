using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Runs genuine native Prepare refusal, original receipt replay and changed-body refusal.</summary>
internal static class ControlledPartitionMovementNativePrepareFlow
{
    private const int Version = 1;
    private const int FirstVoter = 0;
    private const long InitialEpoch = 1;
    private const long InitialRevision = 0;
    private const long ChangedRevision = 1;

    internal static async Task<(PartitionMovementTransportRequest Original, PartitionMovePhaseResult Phase)> ExecuteAsync(ControlledPartitionMovementNativeHosts owners,
        CancellationToken cancellationToken)
    {
        var source = owners.Source;
        var placement = new AtomicPartitionPlacementResolution(Version, ControlledPartitionMovementCorpus.Partition,
            owners.Control.Owner.PhysicalShardId, owners.Control.Owner.Incarnation, owners.Control.Owner.VoterIds,
            InitialEpoch, InitialRevision, InitialRevision, true);
        var request = ControlledPartitionMovementNativeRequest.Create(source.Database, source.Runtime,
            owners.Control.Owner, owners.Destination.Owner, placement, source.Partition.Configuration.LocalId,
            SiloAddress.FromParsableString(source.CallerSiloAddress));
        await ControlledPartitionMovementNativeProofFlow.RejectAsync(source, owners.Target, owners.Control,
            request, cancellationToken);
        using var transport = source.BorrowTransport();
        var reply = await ControlledPartitionMovementNativeSend.PhaseAsync(transport, FirstVoter, owners.Control,
            request, local: true, cancellationToken);
        var entry = await ControlledPartitionMovementNativeEntryReader.ReadAsync(source, request.CommandId, cancellationToken);
        await Assert.That(entry.Term).IsGreaterThan(InitialRevision);
        await Assert.That(entry.Operation?.PrincipalId).IsEqualTo(PhysicalShardCatalogFixture.RootPrincipalId);
        var expected = await ControlledPartitionMovementNativeReplyAssertions.PreparedAsync(reply, placement,
            owners.Control.Owner, owners.Destination.Owner, entry.Index);
        await ReplayAsync(owners, request, expected, cancellationToken);
        var changed = ControlledPartitionMovementNativeRequest.Create(source.Database, source.Runtime,
            owners.Control.Owner, owners.Destination.Owner, placement, source.Partition.Configuration.LocalId,
            SiloAddress.FromParsableString(source.CallerSiloAddress), ChangedRevision);
        var sourceImage = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var targetImage = ControlledPartitionMovementRawImage.Bytes(owners.Target.Store);
        var sourcePosition = source.Store.Position;
        var targetPosition = owners.Target.Store.Position;
        var sourceIndex = source.Log.State.LastIndex;
        var targetIndex = owners.Target.Log.State.LastIndex;
        var conflict = await ControlledPartitionMovementNativeSend.PhaseAsync(transport, FirstVoter, owners.Control,
            changed, local: true, cancellationToken);
        await ControlledPartitionMovementNativeReplyAssertions.RefusedAsync(conflict, ErrorCode.Conflict);
        await Assert.That(source.Store.Position).IsEqualTo(sourcePosition);
        await Assert.That(owners.Target.Store.Position).IsEqualTo(targetPosition);
        await Assert.That(source.Log.State.LastIndex).IsEqualTo(sourceIndex);
        await Assert.That(owners.Target.Log.State.LastIndex).IsEqualTo(targetIndex);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(sourceImage)).IsTrue();
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(owners.Target.Store).SequenceEqual(targetImage)).IsTrue();
        await ReplayAsync(owners, request, expected, cancellationToken);
        return (request, expected);
    }

    internal static async Task ReplayAsync(ControlledPartitionMovementNativeHosts owners,
        PartitionMovementTransportRequest original, PartitionMovePhaseResult expected, CancellationToken cancellationToken)
    {
        using var transport = owners.Source.BorrowTransport();
        var replay = original with
        {
            CallerSiloAddress = owners.Source.CallerSiloAddress,
            Envelope = original.Envelope with { Nonce = Guid.NewGuid() }
        };
        var reply = await ControlledPartitionMovementNativeSend.PhaseAsync(transport, FirstVoter, owners.Control,
            replay, local: true, cancellationToken);
        await Assert.That(reply.Value.Reply.Error).IsNull();
        await Assert.That(reply.Value.Reply.SafeDetail).IsNull();
        var phase = GrainNativePayload.Read<GrainValue>(reply.Value.Reply.Payload).Value as PartitionMovePhaseResult;
        await Assert.That(JsonDefaults.Serialize(phase).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task ReadOutcomeAsync(ControlledPartitionMovementNativeHosts owners,
        PartitionMovementTransportRequest original, PartitionMovePhaseResult expected, CancellationToken cancellationToken)
    {
        var source = owners.Source;
        var limits = source.Database.Limits;
        var query = new PartitionMovementOutcomeTransportRequest(original,
            source.Clock.GetUtcNow() + source.Runtime.GrainRouting.Value.RequestLifetime, Guid.NewGuid(),
            Math.Min(limits.MaxBatchBytes, limits.MaxQueryReadBytes),
            Math.Min(limits.MaxBatchMutations, limits.MaxScanRecords), limits.MaxBatchBytes);
        using var transport = source.BorrowTransport();
        var reply = await ControlledPartitionMovementNativeSend.OutcomeAsync(transport, FirstVoter, owners.Control,
            query, local: true, cancellationToken);
        await Assert.That(reply.Value.Reply.Error).IsNull();
        await Assert.That(reply.Value.Reply.SafeDetail).IsNull();
        var witness = GrainNativePayload.Read<GrainValue>(reply.Value.Reply.Payload).Value
            as PartitionMovementOutcomeWitness;
        await Assert.That(witness).IsNotNull();
        var actual = witness ?? throw new InvalidOperationException("The genuine outcome read returned no witness.");
        await Assert.That(actual.ReadBytes).IsGreaterThan(InitialRevision);
        await Assert.That(actual.ReadBytes <= query.MaximumReadBytes).IsTrue();
        await Assert.That(actual.ExaminedRecords).IsGreaterThan(FirstVoter);
        await Assert.That(actual.ExaminedRecords <= query.MaximumExaminedRecords).IsTrue();
        await Assert.That(actual.Result.Error).IsNull();
        await Assert.That(actual.Result.SafeDetail).IsNull();
        await Assert.That(JsonDefaults.Serialize(actual.Result.Get<PartitionMovePhaseResult>()).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

}
