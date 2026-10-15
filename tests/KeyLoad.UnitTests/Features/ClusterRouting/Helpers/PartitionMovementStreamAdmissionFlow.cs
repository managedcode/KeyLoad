using KeyLoad.Clustering;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Protocol-only controls around actual signed purpose binding and native chunk serialization.</summary>
internal static class PartitionMovementStreamAdmissionFlow
{
    private const string Root = "root";
    private const string WrongMetadata = "unrecognized-movement-metadata";
    private const int FirstProgress = 2;
    private const int RepeatedProgress = 3;
    private const int TerminalAfterRepeats = 4;
    private const int FrameLimit = 4;
    private const int OneMoreFrame = 1;
    private const byte PayloadMarker = 1;

    internal static async Task SuffixAsync(RequestCqrsClusterFixture fixture, PartitionMovePhase phase)
    {
        var id = Guid.NewGuid();
        var admission = Admission(fixture, id);
        admission.Admit(RequestCqrsProtocolCases.StartedChunk(id), CancellationToken.None);
        admission.Admit(Progress(id, phase, FirstProgress), CancellationToken.None);
        admission.Admit(Progress(id, phase, RepeatedProgress), CancellationToken.None);
        var terminal = RequestCqrsProtocolCases.CompletedChunk(new byte[] { PayloadMarker }, TerminalAfterRepeats);
        admission.Admit(terminal, CancellationToken.None);
        await Assert.That(admission.CompletionFailure(CancellationToken.None)).IsNull();
        var duplicate = Assert.ThrowsExactly<KeyLoadException>(() => admission.Admit(terminal, CancellationToken.None));
        await Assert.That(duplicate.Code).IsEqualTo(ErrorCode.OwnershipLost);
    }

    internal static async Task RefusedAsync(RequestCqrsClusterFixture fixture, PartitionMovementStreamFault fault)
    {
        var id = Guid.NewGuid();
        var purpose = fault == PartitionMovementStreamFault.Unbound ? new NativeCqrsStreamPurpose()
            : Purpose(fixture, id, fault == PartitionMovementStreamFault.OtherOperation ? OperationKind.Batch : OperationKind.MovePartition);
        var admission = Admission(fixture, id, purpose: purpose);
        admission.Admit(RequestCqrsProtocolCases.StartedChunk(id), CancellationToken.None);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => admission.Admit(Invalid(id, fault), CancellationToken.None));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(failure.Message).IsEqualTo(GrainRoutingProtocol.InvalidRequest);
    }

    internal static async Task QuotaAndReplayAsync(RequestCqrsClusterFixture fixture)
    {
        var id = Guid.NewGuid();
        var replay = Admission(fixture, id);
        replay.Admit(RequestCqrsProtocolCases.StartedChunk(id), CancellationToken.None);
        replay.Admit(RequestCqrsProtocolCases.CompletedChunk(new byte[] { PayloadMarker }), CancellationToken.None);
        await Assert.That(replay.CompletionFailure(CancellationToken.None)).IsNull();
        var options = UnitRoutingOptions.Routing(new GrainRoutingOptions
        {
            MaximumTotalFrames = FrameLimit,
            MaximumRequestProducers = FrameLimit
        });
        var bounded = Admission(fixture, id, options);
        bounded.Admit(RequestCqrsProtocolCases.StartedChunk(id), CancellationToken.None);
        for (var sequence = FirstProgress; sequence <= FrameLimit; sequence++)
        {
            bounded.Admit(Progress(id, PartitionMovePhase.Prepared, sequence), CancellationToken.None);
        }
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => bounded.Admit(
            Progress(id, PartitionMovePhase.Prepared, FrameLimit + OneMoreFrame), CancellationToken.None));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(bounded.CompletionFailure(CancellationToken.None)).IsTypeOf<KeyLoadException>();
    }

    private static NativeCqrsStreamAdmission Admission(RequestCqrsClusterFixture fixture, Guid id,
        IOptions<GrainRoutingOptions>? options = null, NativeCqrsStreamPurpose? purpose = null)
        => new(fixture.Cluster.ServiceProvider.GetRequiredService<
            Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(), id,
            options ?? fixture.RoutingOptions, purpose ?? Purpose(fixture, id, OperationKind.MovePartition));

    private static NativeCqrsStreamPurpose Purpose(RequestCqrsClusterFixture fixture, Guid id, OperationKind kind)
    {
        var moveId = Guid.NewGuid();
        var request = new PartitionMoveRequest(moveId, fixture.Database.Partition, Guid.NewGuid(),
            OneMoreFrame, PartitionMoveMode.Transfer);
        var signed = fixture.Codec.CreateCommand(id, Root, kind, moveId, NativeSerialization.Serialize(request));
        var purpose = new NativeCqrsStreamPurpose();
        purpose.Bind(fixture.Codec.VerifyRequest(signed, id));
        return purpose;
    }

    private static CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> Progress(Guid id, PartitionMovePhase phase,
        long sequence)
        => new(CqrsStreamChunkKind.Progress, Result<GrainRequestProgress>.Succeed(new GrainRequestProgress(id)
        {
            MovePhase = phase
        }), null, PartitionMovementStreamShape.PhaseCompleted, null, null, sequence);

    private static CqrsStreamChunk<GrainRequestProgress, GrainOperationReply> Invalid(Guid id, PartitionMovementStreamFault fault)
    {
        var phase = fault switch
        {
            PartitionMovementStreamFault.NonePhase => PartitionMovePhase.None,
            PartitionMovementStreamFault.RetiredPhase => PartitionMovePhase.Retired,
            PartitionMovementStreamFault.AbortedPhase => PartitionMovePhase.Aborted,
            PartitionMovementStreamFault.UnknownPhase => (PartitionMovePhase)int.MaxValue,
            _ => PartitionMovePhase.Prepared
        };
        var progress = new GrainRequestProgress(fault == PartitionMovementStreamFault.WrongRequest ? Guid.NewGuid() : id)
        {
            MovePhase = phase,
            TextPhase = fault == PartitionMovementStreamFault.MixedPhase ? TextIndexMaintenancePhase.Configure : null
        };
        var final = fault == PartitionMovementStreamFault.FinalAttached
            ? Result<GrainOperationReply>.Succeed(new GrainOperationReply { Payload = new byte[] { PayloadMarker } })
            : (Result<GrainOperationReply>?)null;
        return new(CqrsStreamChunkKind.Progress, Result<GrainRequestProgress>.Succeed(progress), final,
            fault == PartitionMovementStreamFault.WrongMessage ? WrongMetadata : PartitionMovementStreamShape.PhaseCompleted,
            fault == PartitionMovementStreamFault.WrongEventType ? WrongMetadata : null,
            fault == PartitionMovementStreamFault.EventId ? WrongMetadata : null,
            fault == PartitionMovementStreamFault.WrongSequence ? RepeatedProgress : FirstProgress);
    }
}
