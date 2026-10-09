using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class MovementFrameReplicaObservation
{
    private const int InitialPageOrdinal = 0;
    private const long UnobservedPrefix = 0;
    private const int UnobservedCap = 0;
    private readonly DatabaseEngine database;
    private readonly Guid physical;
    private readonly IOptions<ReplicaConfiguration> replicaOptions;
    private readonly MovementFrameObservationFiles files;
    private readonly AsyncLocal<MovementFrameReplicaObservationScope?> current = new();
    private readonly Lock sync = new();
    private MovementFrameReplicaObservationScope? active;
    private bool closed;

    internal MovementFrameReplicaObservation(IOptions<NodeOptions> nodeOptions, IOptions<ReplicaConfiguration> replicaOptions, DatabaseEngine database,
        IOptions<RequestProbeExecutionOptions> options)
    {
        this.database = database;
        this.replicaOptions = replicaOptions;
        physical = nodeOptions.Value.PhysicalShardId;
        files = new(nodeOptions.Value.MovementFrameObservation, replicaOptions.Value.LocalId, options);
    }
    internal IDisposable? Enter(ReplicaEntry entry)
    {
        if (entry.Operation is not { Kind: OperationKind.PartitionMovementPhase } operation)
        { return null; }
        var selection = files.ReadSelection();
        if (selection is null)
        { return null; }
        var phase = NativeCommandPayload.Read<PartitionMovePhaseCommand>(operation);
        if (phase.Stage != PartitionMovePeerStage.Install || phase.ReceiverEffectAdmission is not { } admission)
        { return null; }
        var body = NativeSerialization.Deserialize<PartitionMoveInstallBody>(phase.Body.Span);
        var totalPages = InitialPageOrdinal;
        foreach (var family in body.Descriptor.Families)
        { totalPages = checked(totalPages + family.PageCount); }
        if (phase.PageOrdinal != totalPages || phase.MoveId != selection.Value.MoveId
            || phase.Partition != selection.Value.Partition.ToPartition()
            || body.OperatorPrincipalId != selection.Value.OperatorPrincipalId
            || selection.Value.PhysicalShardId != physical || selection.Value.Incarnation != replicaOptions.Value.Incarnation
            || phase.DestinationOwner.PhysicalShardId != physical || phase.DestinationOwner.Incarnation != replicaOptions.Value.Incarnation)
        { return null; }
        var identity = new MovementFrameObservationRecord(MovementFrameObservationProtocol.Version,
            MovementFrameObservationProtocol.ObservationKind, selection.Value.SessionId, selection.Value.SelectionId,
            phase.MoveId, new(phase.Partition.TenantId, phase.Partition.DatabaseId,
                phase.Partition.TransactionDomainId, phase.Partition.PartitionKey), body.OperatorPrincipalId,
            operation.PrincipalId, physical, replicaOptions.Value.Incarnation, replicaOptions.Value.LocalId, operation.Id,
            admission.OriginalRequestNonce, admission.OriginalExpiresAt, entry.Index, entry.Term,
            UnobservedPrefix, UnobservedCap);
        lock (sync)
        {
            if (closed || active is not null || current.Value is not null)
            { throw Invalid(); }
            var scope = new MovementFrameReplicaObservationScope(this, entry, identity);
            active = current.Value = scope;
            return scope;
        }
    }
    internal void Observe(CommitStage stage, long prefix, int cap) => current.Value?.Observe(stage, prefix, cap);
    internal void Complete(ReplicaEntry entry, MovementFrameObservationRecord value)
    {
        if (entry.Operation is not { } original || original.Id != value.EffectCommandId
            || original.PrincipalId != value.ReceiverPrincipalId || entry.Index != value.EntryIndex
            || entry.Term != value.EntryTerm || value.PhysicalShardId != physical || value.Incarnation != replicaOptions.Value.Incarnation)
        { throw Invalid(); }
        var outcome = database.ResolveOutcome(original);
        if (!ZoneTreeStore.IsEncodedFrameLimitRejection(outcome))
        { throw Invalid(); }
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => files.Write(value), failures);
        if (failures.Count > PartitionMoveProtocol.EmptyCount)
        {
            failures.Insert(PartitionMoveProtocol.EmptyCount, Errors.Fail(ErrorCode.ResourceExhausted,
                outcome!.SafeDetail ?? MovementFrameObservationProtocol.Invalid));
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }
    internal void Exit(MovementFrameReplicaObservationScope scope)
    {
        lock (sync)
        {
            if (!ReferenceEquals(active, scope) || !ReferenceEquals(current.Value, scope))
            { throw Invalid(); }
            active = current.Value = null;
        }
    }
    internal void Close()
    {
        lock (sync)
        {
            if (active is not null)
            { throw Invalid(); }
            closed = true;
        }
    }
    private static InvalidOperationException Invalid() => new(MovementFrameObservationProtocol.Invalid);
}
