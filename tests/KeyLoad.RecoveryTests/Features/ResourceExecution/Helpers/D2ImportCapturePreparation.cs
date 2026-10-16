using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.CrashHost.Features.ResourceExecution;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2ImportCapturePreparation
{
    private const long InitialRevision = 0;

    internal static async Task<PartitionMovePhaseResult> ExecuteAsync(D2ImportSession session,
        PartitionRef partition, CancellationToken token)
    {
        var source = session.Source;
        var placement = source.Database.ReadAtomicPartitionPlacement(D2ImportCrashProtocol.Principal, new(D2ImportCrashProtocol.Version, partition));
        var moveId = Guid.NewGuid();
        var body = NativeSerialization.Serialize(new PartitionMovePrepareBody(D2ImportCrashProtocol.Principal,
            new(moveId, partition, session.Owners.Destination.Owner.PhysicalShardId,
                InitialRevision, PartitionMoveMode.Transfer)));
        var envelope = new PartitionMovePeerEnvelope(D2ImportCrashProtocol.Version, moveId, partition,
            session.Owners.Control.Owner, placement, session.Owners.Destination.Owner,
            Convert.ToHexStringLower(SHA256.HashData(body)), PartitionMovePeerStage.ControlPrepare,
            D2ImportCrashProtocol.Empty, session.ExpiresAt, Guid.NewGuid(), body);
        var input = new PartitionMovementTransportRequest(Guid.NewGuid(), envelope, null,
            session.Owners.Control.Owner.VoterIds.First(), session.Caller,
            PartitionMovementTransportAction.Apply, Guid.Empty, D2ImportCrashProtocol.Empty);
        var operation = await ControlledPartitionMovementProcessIssuer.IssueAsync(source,
            session.SourceAdmission, input, token);
        var prepared = source.Journal.Submit(operation, token).Get<PartitionMovePhaseResult>();
        var fenced = await D2ImportPhases.EffectAsync(session, prepared,
            D2ImportPhases.Phase(session, prepared, PartitionMovePeerStage.Fence,
                NativeSerialization.Serialize(new PartitionMoveControlBody(D2ImportCrashProtocol.Principal, prepared.Control!))),
            atSource: true, token);
        return await D2ImportPhases.LocalAsync(session, D2ImportPhases.Phase(session, prepared,
            PartitionMovePeerStage.ControlAcceptFence, NativeSerialization.Serialize(new PartitionMoveFenceAcceptBody(
                D2ImportCrashProtocol.Principal, prepared.Control!, fenced.Result.Fence!, fenced.GrantId))), token);
    }
}
