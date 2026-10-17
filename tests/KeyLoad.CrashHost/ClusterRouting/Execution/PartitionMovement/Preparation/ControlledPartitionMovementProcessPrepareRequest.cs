using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Builds original unverified prepare input from actual bound listener and persisted owner scope.</summary>
internal static class ControlledPartitionMovementProcessPrepareRequest
{
    private const string MoveIdText = "3a2e286c-c0a0-4970-a867-58c8ebd83927";
    private const string CommandIdText = "ce9ea8ca-4a17-42aa-aa7f-c93b322e72e5";
    internal static readonly Guid MoveId = Guid.Parse(MoveIdText);
    internal static readonly Guid CommandId = Guid.Parse(CommandIdText);
    private const string PrincipalId = "root";
    private const int Version = 1;
    private const int PageOrdinal = 0;
    private const int InitialOrdinal = 0;
    private const long InitialRevision = 0;

    internal static string CallerAddress(ControlledPartitionMovementLoopbackListeners listeners)
        => SiloAddress.New(listeners.SiloEndpoint, SiloAddress.AllocateNewGeneration()).ToParsableString();

    internal static PartitionMovementTransportRequest Create(ControlledPartitionMovementNativeNode source,
        ServerRuntimeOptions runtime, ControlledPartitionMovementProcessOwners corpus,
        AtomicPartitionPlacementResolution placement, string callerAddress, long expectedRevision = InitialRevision)
    {
        var request = new PartitionMoveRequest(MoveId, ControlledPartitionMovementProcessCorpus.Partition,
            corpus.Destination.Owner.PhysicalShardId, expectedRevision, PartitionMoveMode.Transfer);
        var body = NativeSerialization.Serialize(new PartitionMovePrepareBody(PrincipalId, request));
        var envelope = new PartitionMovePeerEnvelope(Version, MoveId, request.Partition,
            corpus.Control.Owner, placement, corpus.Destination.Owner, Convert.ToHexStringLower(SHA256.HashData(body)),
            PartitionMovePeerStage.ControlPrepare, PageOrdinal,
            source.Database.EvaluationClock.GetUtcNow() + runtime.GrainRouting.Value.RequestLifetime,
            Guid.NewGuid(), body);
        return new(CommandId, envelope, null, corpus.Control.Owner.VoterIds.First(), callerAddress,
            PartitionMovementTransportAction.Apply, Guid.Empty, InitialOrdinal);
    }
}
